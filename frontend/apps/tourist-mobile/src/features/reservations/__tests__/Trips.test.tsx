import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { FlatList } from 'react-native'
import TripsScreen from '@/../app/(tabs)/trips'
import { useSession } from '@/auth/session'
import {
  createTestQueryClient,
  guestSession,
  itemFixture,
  ok,
  pagedFixture,
  reservationFixture,
  touristSession,
  withClient,
} from '@/test-utils'

const mockGet = jest.fn()
jest.mock('@/lib/httpClient', () => ({ httpClient: { get: (...args: unknown[]) => mockGet(...args), post: jest.fn() } }))

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>
let client = createTestQueryClient()

function renderTrips() {
  const Wrapper = withClient(client)
  return render(
    <Wrapper>
      <TripsScreen />
    </Wrapper>,
  )
}

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  mockedUseSession.mockReturnValue(touristSession())
})

afterEach(() => client.clear())

it('invitado: invitación amigable a iniciar sesión, sin pedir datos privados', () => {
  mockedUseSession.mockReturnValue(guestSession())
  renderTrips()

  expect(screen.getByText('Iniciá sesión para ver tus viajes')).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Iniciar sesión' })).toBeTruthy()
  expect(screen.getByRole('button', { name: 'Crear cuenta' })).toBeTruthy()
  expect(mockGet).not.toHaveBeenCalled()
})

it('sin reservas: estado vacío explícito', async () => {
  mockGet.mockReturnValue(ok(pagedFixture([])))
  renderTrips()

  await waitFor(() => expect(screen.getByText('Todavía no tenés viajes')).toBeTruthy())
})

it('pide la lista real con paginación y muestra los datos de cada reserva', async () => {
  mockGet.mockReturnValue(ok(pagedFixture([reservationFixture({ id: 'r1' })])))
  renderTrips()

  await waitFor(() => expect(screen.getByText('Tour Illimani')).toBeTruthy())
  expect(mockGet).toHaveBeenCalledWith('/api/reservations/me', { params: { page: 1, pageSize: 20 } })
  expect(screen.getByText('Andes Tours')).toBeTruthy()
  expect(screen.getByText('2 viajeros')).toBeTruthy()
  expect(screen.getByText(/09:00/)).toBeTruthy()
  expect(screen.getByText('Pendiente de pago')).toBeTruthy()
  expect(screen.getByText(/USD\s+80\.00/)).toBeTruthy()
})

it('scroll infinito: al llegar al final pide la página siguiente y acumula', async () => {
  mockGet
    .mockReturnValueOnce(ok(pagedFixture([reservationFixture({ id: 'r1', items: [itemFixture({ experienceTitle: 'Viaje uno' })] })], 1, 2)))
    .mockReturnValueOnce(ok(pagedFixture([reservationFixture({ id: 'r2', items: [itemFixture({ experienceTitle: 'Viaje dos' })] })], 2, 2)))
  renderTrips()
  await waitFor(() => expect(screen.getByText('Viaje uno')).toBeTruthy())

  await act(async () => {
    fireEvent(screen.UNSAFE_getByType(FlatList), 'onEndReached')
  })

  await waitFor(() => expect(screen.getByText('Viaje dos')).toBeTruthy())
  expect(mockGet).toHaveBeenLastCalledWith('/api/reservations/me', { params: { page: 2, pageSize: 20 } })
  expect(screen.getByText('Viaje uno')).toBeTruthy()
})

it('multi-ítem: primer producto, "+N más" y cantidad de servicios', async () => {
  mockGet.mockReturnValue(
    ok(
      pagedFixture([
        reservationFixture({
          id: 'r1',
          status: 'CONFIRMED',
          items: [
            itemFixture({ id: 'a', status: 'CONFIRMED' }),
            itemFixture({ id: 'b', status: 'CONFIRMED', productType: 'PACKAGE', experienceTitle: null, packageTitle: 'Uyuni 3 días' }),
          ],
        }),
      ]),
    ),
  )
  renderTrips()

  await waitFor(() => expect(screen.getByText(/Tour Illimani/)).toBeTruthy())
  expect(screen.getByText(/\+1 más/)).toBeTruthy()
  expect(screen.getByText('2 servicios')).toBeTruthy()
})

it('multi-moneda: una fila por moneda en la tarjeta, sin sumarlas', async () => {
  mockGet.mockReturnValue(
    ok(
      pagedFixture([
        reservationFixture({
          id: 'r1',
          status: 'CONFIRMED',
          items: [itemFixture({ status: 'CONFIRMED' }), itemFixture({ id: 'b', status: 'CONFIRMED', currency: 'BOB' })],
          totals: [
            { currency: 'USD', amount: 120 },
            { currency: 'BOB', amount: 350 },
          ],
        }),
      ]),
    ),
  )
  renderTrips()

  await waitFor(() => expect(screen.getByText(/USD\s+120\.00/)).toBeTruthy())
  expect(screen.getByText(/BOB\s+350\.00/)).toBeTruthy()
  expect(screen.queryByText(/470/)).toBeNull()
})

it('badges para cada estado, incluido el fallback neutral de PAYMENT_FAILED', async () => {
  mockGet.mockReturnValue(
    ok(
      pagedFixture([
        reservationFixture({ id: 'a', status: 'CONFIRMED', items: [itemFixture({ status: 'CONFIRMED' })] }),
        reservationFixture({ id: 'b', status: 'EXPIRED', items: [itemFixture({ status: 'EXPIRED' })] }),
        reservationFixture({ id: 'c', status: 'CANCELLED', items: [itemFixture({ status: 'CANCELLED' })] }),
        reservationFixture({ id: 'd', status: 'PAYMENT_FAILED' }),
        reservationFixture({ id: 'e', status: 'CONFIRMED', items: [itemFixture({ status: 'CANCELLED', cancellationReason: 'Mal clima' })] }),
      ]),
    ),
  )
  renderTrips()

  await waitFor(() => expect(screen.getByText('Confirmada')).toBeTruthy())
  expect(screen.getByText('Expirada')).toBeTruthy()
  expect(screen.getByText('Cancelada')).toBeTruthy()
  expect(screen.getByText('Estado no disponible')).toBeTruthy()
  expect(screen.getByText('Cancelada por el operador')).toBeTruthy()
})

it('tarjeta pendiente: "Pagar" lleva al checkout y la tarjeta al detalle', async () => {
  mockGet.mockReturnValue(ok(pagedFixture([reservationFixture({ id: 'r9' })])))
  renderTrips()
  await waitFor(() => expect(screen.getByRole('button', { name: 'Pagar' })).toBeTruthy())

  fireEvent.press(screen.getByRole('button', { name: 'Pagar' }))
  expect(mockRouter.push).toHaveBeenCalledWith('/checkout/r9')

  fireEvent.press(screen.getByLabelText(/Tour Illimani\. Pendiente de pago/))
  expect(mockRouter.push).toHaveBeenCalledWith('/reservation/r9')
})
