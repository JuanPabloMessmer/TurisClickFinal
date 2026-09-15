import { act, render, screen, waitFor } from '@testing-library/react-native'
import TripsScreen from '@/../app/(tabs)/trips'
import { useSession } from '@/auth/session'
import { SessionQuerySync } from '@/auth/SessionQuerySync'
import {
  createTestQueryClient,
  deferred,
  guestSession,
  itemFixture,
  ok,
  pagedFixture,
  reservationFixture,
  touristSession,
  withClient,
} from '@/test-utils'

/**
 * Caché privada entre sesiones en el MISMO teléfono. Es obligatorio: la segunda persona que inicia
 * sesión nunca puede ver, ni por un instante, los viajes de la primera.
 */

const mockGet = jest.fn()
jest.mock('@/lib/httpClient', () => ({ httpClient: { get: (...args: unknown[]) => mockGet(...args), post: jest.fn() } }))
jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn(), replace: jest.fn() }) }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

const tripsOf = (title: string) => pagedFixture([reservationFixture({ id: `r-${title}`, items: [itemFixture({ experienceTitle: title })] })])

let client = createTestQueryClient()
/**
 * Un solo Wrapper por test, igual que en la app, donde el provider se monta una única vez. Si `rerender`
 * recibiera un Wrapper nuevo, React remontaría el árbol y SessionQuerySync perdería el usuario anterior.
 */
let Wrapper = withClient(client)

function app() {
  return (
    <Wrapper>
      <SessionQuerySync />
      <TripsScreen />
    </Wrapper>
  )
}

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  Wrapper = withClient(client)
})

afterEach(() => client.clear())

it('Tourist A ve sus viajes → logout → Tourist B nunca ve los de A', async () => {
  // Datos públicos de catálogo en caché: NO deben borrarse.
  client.setQueryData(['catalog', 'categories'], [{ id: 'c1', name: 'Aventura' }])

  // ---- Tourist A ----
  mockedUseSession.mockReturnValue(touristSession('user-a', 'Ana Quispe'))
  mockGet.mockReturnValue(ok(tripsOf('Viaje de Ana')))
  const { rerender } = render(app())
  await waitFor(() => expect(screen.getByText('Viaje de Ana')).toBeTruthy())

  // ---- Logout ----
  mockedUseSession.mockReturnValue(guestSession())
  rerender(app())

  expect(screen.queryByText('Viaje de Ana')).toBeNull()
  await waitFor(() => expect(client.getQueryCache().findAll({ queryKey: ['reservations'] })).toHaveLength(0))
  expect(client.getQueryData(['catalog', 'categories'])).toEqual([{ id: 'c1', name: 'Aventura' }])

  // ---- Tourist B: su lista todavía no llegó ----
  const bTrips = deferred<{ data: unknown }>()
  mockGet.mockReturnValue(bTrips.promise)
  mockedUseSession.mockReturnValue(touristSession('user-b', 'Bruno Mamani'))
  rerender(app())

  // Mientras B carga, lo de A no aparece en ningún momento.
  expect(screen.queryByText('Viaje de Ana')).toBeNull()
  await act(async () => {})
  expect(screen.queryByText('Viaje de Ana')).toBeNull()

  await act(async () => bTrips.resolve({ data: tripsOf('Viaje de Bruno') }))

  await waitFor(() => expect(screen.getByText('Viaje de Bruno')).toBeTruthy())
  expect(screen.queryByText('Viaje de Ana')).toBeNull()
  expect(client.getQueryCache().findAll({ queryKey: ['reservations', 'user-a'] })).toHaveLength(0)
})

it('un refresh fallido que deja la sesión vacía también borra la caché privada', async () => {
  mockedUseSession.mockReturnValue(touristSession('user-a'))
  mockGet.mockReturnValue(ok(tripsOf('Viaje de Ana')))
  const { rerender } = render(app())
  await waitFor(() => expect(screen.getByText('Viaje de Ana')).toBeTruthy())

  // AuthManager.clearSession() tras un refresh fallido deja el mismo estado que un logout.
  mockedUseSession.mockReturnValue(guestSession())
  rerender(app())

  await waitFor(() => expect(client.getQueryCache().findAll({ queryKey: ['reservations'] })).toHaveLength(0))
  expect(screen.getByText('Tu sesión expiró')).toBeTruthy()
})

/**
 * Regresión de un bug encontrado en esta fase: limpiar también en invitado → turista cancelaba y borraba
 * la primera carga de Mis viajes del turista que recién entraba, y la lista quedaba en skeleton.
 */
it('invitado en Mis viajes inicia sesión → su lista carga (no la cancela la limpieza)', async () => {
  mockedUseSession.mockReturnValue(guestSession())
  const { rerender } = render(app())
  expect(screen.getByText('Iniciá sesión para ver tus viajes')).toBeTruthy()

  const trips = deferred<{ data: unknown }>()
  mockGet.mockReturnValue(trips.promise)
  mockedUseSession.mockReturnValue(touristSession('user-b', 'Bruno Mamani'))
  rerender(app())
  await act(async () => {})

  await act(async () => trips.resolve({ data: tripsOf('Viaje de Bruno') }))

  await waitFor(() => expect(screen.getByText('Viaje de Bruno')).toBeTruthy())
  expect(mockGet).toHaveBeenCalledTimes(1)
})

it('cambio directo de una cuenta a otra borra la caché de la anterior sin cancelar la carga de la nueva', async () => {
  mockedUseSession.mockReturnValue(touristSession('user-a'))
  mockGet.mockReturnValue(ok(tripsOf('Viaje de Ana')))
  const { rerender } = render(app())
  await waitFor(() => expect(screen.getByText('Viaje de Ana')).toBeTruthy())

  mockGet.mockReturnValue(ok(tripsOf('Viaje de Bruno')))
  mockedUseSession.mockReturnValue(touristSession('user-b', 'Bruno Mamani'))
  rerender(app())

  await waitFor(() => expect(client.getQueryCache().findAll({ queryKey: ['reservations', 'user-a'] })).toHaveLength(0))
  expect(screen.queryByText('Viaje de Ana')).toBeNull()
  await waitFor(() => expect(screen.getByText('Viaje de Bruno')).toBeTruthy())
})

it('mientras se inicia sesión (status loading) no borra nada todavía', async () => {
  mockedUseSession.mockReturnValue(touristSession('user-a'))
  mockGet.mockReturnValue(ok(tripsOf('Viaje de Ana')))
  const { rerender } = render(app())
  await waitFor(() => expect(screen.getByText('Viaje de Ana')).toBeTruthy())

  mockedUseSession.mockReturnValue({ ...touristSession('user-a'), status: 'loading', isAuthenticated: false })
  rerender(app())
  await act(async () => {})

  expect(client.getQueryCache().findAll({ queryKey: ['reservations', 'user-a'] }).length).toBeGreaterThan(0)
})
