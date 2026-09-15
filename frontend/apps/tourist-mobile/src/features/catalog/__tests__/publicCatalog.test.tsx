import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import ExperienceDetailScreen from '@/../app/experience/[id]'
import PackageDetailScreen from '@/../app/package/[id]'
import { createTestQueryClient, ok, withClient } from '@/test-utils'

/**
 * Regresión de Fase 1: el catálogo sigue siendo PÚBLICO. Los detalles se ven sin sesión y el CTA de
 * reserva lleva a elegir fecha sin pedir login (la sesión se pide recién al crear la reserva). Estos
 * detalles ni siquiera dependen de la sesión: no hay un guard que pueda bloquearlos.
 */

const mockGet = jest.fn()
jest.mock('@/lib/httpClient', () => ({ httpClient: { get: (...args: unknown[]) => mockGet(...args) } }))

const mockRouter = { push: jest.fn(), back: jest.fn() }
let mockParams: Record<string, string> = {}
jest.mock('expo-router', () => ({
  useRouter: () => mockRouter,
  useLocalSearchParams: () => mockParams,
  Stack: { Screen: () => null },
}))

let client = createTestQueryClient()

function renderScreen(Screen: React.ComponentType) {
  const Wrapper = withClient(client)
  return render(
    <Wrapper>
      <Screen />
    </Wrapper>,
  )
}

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
})

afterEach(() => client.clear())

it('detalle de experiencia: se ve sin sesión y "Elegir fecha" va a reservar sin pasar por login', async () => {
  mockParams = { id: 'e1' }
  mockGet.mockImplementation((url: string) =>
    url.endsWith('/availability')
      ? ok([{ id: 's1', date: '2026-10-10', startTime: '09:00:00', availableSlots: 4 }])
      : ok({ id: 'e1', title: 'Tour Illimani', companyName: 'Andes Tours', price: 40, currency: 'USD', images: [] }),
  )
  renderScreen(ExperienceDetailScreen)

  await waitFor(() => expect(screen.getByText('Tour Illimani')).toBeTruthy())
  await waitFor(() => expect(screen.getByRole('button', { name: 'Elegir fecha' }).props.accessibilityState).toMatchObject({ disabled: false }))
  expect(screen.queryByText('Reservas disponibles próximamente')).toBeNull()

  fireEvent.press(screen.getByRole('button', { name: 'Elegir fecha' }))

  expect(mockRouter.push).toHaveBeenCalledWith('/book/experience/e1')
  expect(mockRouter.push).not.toHaveBeenCalledWith('/(auth)/login')
})

it('detalle de paquete sin salidas: el CTA se deshabilita y lo dice', async () => {
  mockParams = { id: 'p1' }
  mockGet.mockImplementation((url: string) =>
    url.endsWith('/availability') ? ok([]) : ok({ id: 'p1', title: 'Uyuni 3 días', durationDays: 3, price: 480, currency: 'USD', images: [], items: [] }),
  )
  renderScreen(PackageDetailScreen)

  await waitFor(() => expect(screen.getByRole('button', { name: 'Sin salidas disponibles' })).toBeTruthy())
  expect(screen.getByRole('button', { name: 'Sin salidas disponibles' }).props.accessibilityState).toMatchObject({ disabled: true })
})
