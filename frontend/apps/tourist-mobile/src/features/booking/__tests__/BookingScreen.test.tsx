import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { useSession } from '@/auth/session'
import { BookingScreen, type BookingProduct } from '@/features/booking/BookingScreen'
import type { BookableSlot } from '@/features/booking/selection'
import {
  createTestQueryClient,
  deferred,
  guestSession,
  httpError,
  networkError,
  ok,
  reservationFixture,
  touristSession,
  withClient,
} from '@/test-utils'

const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))

const mockRouter = {
  push: jest.fn(),
  replace: jest.fn(),
  back: jest.fn(),
  navigate: jest.fn(),
  canGoBack: jest.fn(() => true),
}
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

const ready = { isPending: false, isError: false, error: null, refetch: jest.fn() }
const product: BookingProduct = { title: 'Tour Illimani', companyName: 'Andes Tours', price: 40, currency: 'USD' }
const experienceSlots: BookableSlot[] = [
  { id: 's1', date: '2026-10-10', time: '09:00:00', availableSlots: 3 },
  { id: 's2', date: '2026-10-11', time: null, availableSlots: 250 },
]

let client = createTestQueryClient()
/**
 * El Wrapper se crea UNA vez por test: si `rerender` recibiera un Wrapper nuevo, React vería otro tipo de
 * componente en la raíz y remontaría todo, perdiendo el estado que justamente se quiere verificar.
 */
let Wrapper = withClient(client)

function ui(props: Partial<React.ComponentProps<typeof BookingScreen>> = {}) {
  return (
    <Wrapper>
      <BookingScreen
        productType="EXPERIENCE"
        productHref="/experience/e1"
        product={product}
        productQuery={ready}
        slots={experienceSlots}
        availabilityQuery={ready}
        {...props}
      />
    </Wrapper>
  )
}

const continueButton = () => screen.getByRole('button', { name: 'Continuar' })
const isDisabled = (element: { props: { accessibilityState?: { disabled?: boolean } } }) =>
  Boolean(element.props.accessibilityState?.disabled)

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  Wrapper = withClient(client)
  mockedUseSession.mockReturnValue(touristSession())
})

afterEach(() => client.clear())

describe('selección de una experiencia', () => {
  it('Continuar está deshabilitado hasta elegir una fecha', () => {
    render(ui())

    expect(isDisabled(continueButton())).toBe(true)

    fireEvent.press(screen.getAllByRole('radio')[0])

    expect(isDisabled(continueButton())).toBe(false)
    expect(screen.getAllByRole('radio')[0].props.accessibilityState).toMatchObject({ selected: true })
  })

  it('muestra el horario de la experiencia en cada fecha', () => {
    render(ui())

    expect(screen.getAllByText(/09:00/).length).toBeGreaterThan(0)
  })

  it('viajeros: mínimo 1 y máximo el cupo de la fecha', () => {
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0]) // 3 lugares

    expect(isDisabled(screen.getByLabelText('Quitar un viajero'))).toBe(true)

    for (let i = 0; i < 5; i++) fireEvent.press(screen.getByLabelText('Agregar un viajero'))

    expect(screen.getByLabelText('3 viajeros')).toBeTruthy()
    expect(isDisabled(screen.getByLabelText('Agregar un viajero'))).toBe(true)
    expect(screen.getByText('Máximo 3 para esta fecha.')).toBeTruthy()
  })

  it('viajeros: el máximo nunca supera 100 aunque haya más cupo', () => {
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[1]) // 250 lugares

    expect(screen.getByText('Máximo 100 para esta fecha.')).toBeTruthy()
  })

  it('al cambiar a una fecha con menos cupo, ajusta los viajeros sin perder la elección', () => {
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[1])
    for (let i = 0; i < 4; i++) fireEvent.press(screen.getByLabelText('Agregar un viajero')) // 5

    fireEvent.press(screen.getAllByRole('radio')[0]) // 3 lugares

    expect(screen.getByLabelText('3 viajeros')).toBeTruthy()
  })

  it('muestra el precio estimado como estimado, en la moneda del producto', () => {
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])
    fireEvent.press(screen.getByLabelText('Agregar un viajero'))

    expect(screen.getByText(/USD\s+80\.00/)).toBeTruthy()
    expect(screen.getByText('Estimado: se confirma al reservar')).toBeTruthy()
  })

  it('crea la reserva con experienceAvailabilityId y reemplaza la pantalla por el checkout', async () => {
    mockPost.mockReturnValue(ok(reservationFixture({ id: 'r-new' })))
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])
    fireEvent.press(screen.getByLabelText('Agregar un viajero'))

    fireEvent.press(continueButton())

    await waitFor(() => expect(mockRouter.replace).toHaveBeenCalled())
    expect(mockPost).toHaveBeenCalledWith('/api/reservations', { experienceAvailabilityId: 's1', travelers: 2 })
    expect(mockRouter.replace).toHaveBeenCalledWith({
      pathname: '/checkout/[id]',
      params: { id: 'r-new', quotedPrice: '40', quotedCurrency: 'USD' },
    })
    expect(mockRouter.push).not.toHaveBeenCalled()
  })
})

describe('selección de un paquete', () => {
  it('habla de salidas y crea con packageAvailabilityId', async () => {
    mockPost.mockReturnValue(ok(reservationFixture({ id: 'r-pkg' })))
    render(
      ui({
        productType: 'PACKAGE',
        productHref: '/package/p1',
        product: { title: 'Uyuni 3 días', price: 480, currency: 'USD' },
        slots: [{ id: 'd1', date: '2026-11-01', availableSlots: 6 }],
      }),
    )

    expect(screen.getByText('Elegir salida')).toBeTruthy()
    expect(screen.getByText('Salidas disponibles')).toBeTruthy()

    fireEvent.press(screen.getAllByRole('radio')[0])
    expect(screen.getByText('Máximo 6 para esta salida.')).toBeTruthy()
    expect(screen.getByText(/USD\s+480\.00/)).toBeTruthy()

    fireEvent.press(continueButton())

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/reservations', { packageAvailabilityId: 'd1', travelers: 1 }))
  })
})

describe('invitado', () => {
  it('Continuar lleva a login y NO crea ninguna reserva', () => {
    mockedUseSession.mockReturnValue(guestSession())
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])

    fireEvent.press(continueButton())

    expect(mockRouter.push).toHaveBeenCalledWith('/(auth)/login')
    expect(mockPost).not.toHaveBeenCalled()
    expect(screen.getByText('Vas a iniciar sesión antes de reservar.')).toBeTruthy()
  })

  it('al volver de login conserva la selección y no crea la reserva hasta tocar Continuar de nuevo', async () => {
    mockedUseSession.mockReturnValue(guestSession())
    mockPost.mockReturnValue(ok(reservationFixture({ id: 'r-after-login' })))
    const { rerender } = render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])
    fireEvent.press(screen.getByLabelText('Agregar un viajero'))
    fireEvent.press(continueButton())
    expect(mockRouter.push).toHaveBeenCalledWith('/(auth)/login')

    // El login ocurre en un modal encima; al cerrarse, esta misma pantalla sigue montada con sesión.
    mockedUseSession.mockReturnValue(touristSession())
    rerender(ui())

    expect(screen.getAllByRole('radio')[0].props.accessibilityState).toMatchObject({ selected: true })
    expect(screen.getByLabelText('2 viajeros')).toBeTruthy()
    await act(async () => {})
    expect(mockPost).not.toHaveBeenCalled()

    fireEvent.press(continueButton())

    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1))
    expect(mockPost).toHaveBeenCalledWith('/api/reservations', { experienceAvailabilityId: 's1', travelers: 2 })
  })

  it('mientras se restaura la sesión no se puede continuar', () => {
    mockedUseSession.mockReturnValue(guestSession())
    mockedUseSession.mockReturnValue({ ...guestSession(), status: 'idle' })
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])

    expect(isDisabled(continueButton())).toBe(true)
  })
})

describe('protección contra reservas duplicadas', () => {
  it('doble toque en Continuar → una sola request', async () => {
    const pending = deferred<{ data: unknown }>()
    mockPost.mockReturnValue(pending.promise)
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])

    const button = continueButton()
    fireEvent.press(button)
    fireEvent.press(button)
    fireEvent.press(button)

    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1))
    await act(async () => {})
    expect(mockPost).toHaveBeenCalledTimes(1)

    await act(async () => pending.resolve({ data: reservationFixture({ id: 'r1' }) }))
  })

  it('una caída de red NO reintenta el POST y manda a revisar Mis viajes', async () => {
    mockPost.mockRejectedValue(networkError())
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])

    fireEvent.press(continueButton())

    await waitFor(() => expect(screen.getByText(/No pudimos confirmar si la reserva se creó/)).toBeTruthy())
    expect(mockPost).toHaveBeenCalledTimes(1)
    expect(mockRouter.replace).not.toHaveBeenCalled()

    fireEvent.press(screen.getByText('Ir a Mis viajes'))
    expect(mockRouter.push).toHaveBeenCalledWith('/trips')
  })
})

describe('errores reales al crear', () => {
  it('409: sin cupo suficiente, se queda en la pantalla con la selección', async () => {
    mockPost.mockRejectedValue(httpError(409, { detail: 'No hay cupo suficiente para la cantidad de viajeros solicitada.' }))
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])

    fireEvent.press(continueButton())

    await waitFor(() => expect(screen.getByText(/Ya no quedan lugares suficientes para 1 viajero/)).toBeTruthy())
    expect(screen.getAllByRole('radio')[0].props.accessibilityState).toMatchObject({ selected: true })
  })

  it('410: la fecha ya no está disponible y se descarta la selección', async () => {
    mockPost.mockRejectedValue(httpError(410, { detail: 'El slot de disponibilidad ya no está disponible.' }))
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])

    fireEvent.press(continueButton())

    await waitFor(() => expect(screen.getByText('Esa fecha ya no está disponible. Elegí otra.')).toBeTruthy())
    expect(isDisabled(continueButton())).toBe(true)
  })

  it('404: el producto dejó de estar disponible', async () => {
    mockPost.mockRejectedValue(httpError(404, { detail: 'Experiencia no encontrada.' }))
    render(ui())
    fireEvent.press(screen.getAllByRole('radio')[0])

    fireEvent.press(continueButton())

    await waitFor(() => expect(screen.getByText('Este producto ya no está disponible')).toBeTruthy())
  })
})
