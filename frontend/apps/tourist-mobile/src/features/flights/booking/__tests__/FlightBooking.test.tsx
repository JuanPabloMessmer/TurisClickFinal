import { fireEvent, render, screen, waitFor, within } from '@testing-library/react-native'
import BookFlightRoute from '@/../app/book/flight'
import { useSession } from '@/auth/session'
import { FlightResultsScreen } from '@/features/flights/FlightResultsScreen'
import { createTestQueryClient, guestSession, httpError, networkError, ok, reservationFixture, touristSession, withClient } from '@/test-utils'
import { airlineOption, BACK_ID, OUT_ID, providerOption, searchResponse } from '@/test-utils/providerFlights'

/**
 * Reservar un vuelo de operador, de los resultados al checkout, con respuestas simuladas del backend (B2
 * todavía no existe). Lo que se protege:
 * - "Seleccionar" sólo en vuelos de operador reservables; las ofertas de aerolínea, DEMO y sin botón;
 * - buscar y revisar el vuelo no piden sesión; cargar pasajeros sí, y al entrar se vuelve al mismo vuelo;
 * - el pedido es exactamente el contrato B1, sin documento de identidad;
 * - un 501 FEATURE_NOT_AVAILABLE se explica, no se trata como un error genérico.
 */

const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: jest.fn(), post: (...args: unknown[]) => mockPost(...args) },
}))

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }
let mockParams: Record<string, string> = {}
jest.mock('expo-router', () => ({
  useRouter: () => mockRouter,
  useLocalSearchParams: () => mockParams,
  Stack: { Screen: () => null },
}))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

const oneWayParams = { o: 'VVI', d: 'UYU', dep: '2026-11-20', ad: '1', ch: '7', cab: 'ECONOMY', ids: OUT_ID }
const roundTripParams = { ...oneWayParams, ret: '2026-11-24', ch: undefined as unknown as string, inf: '1', ids: `${OUT_ID},${BACK_ID}` }

/** El POST de búsqueda devuelve `search`; el de reservas, lo que diga `reserve`. */
function mockBackend(search: ReturnType<typeof searchResponse>, reserve: () => Promise<unknown> = () => ok(reservationFixture({ id: 'res-9' }))) {
  mockPost.mockImplementation((url: string) => (url === '/api/flights/search' ? ok(search) : reserve()))
}

const reservationCalls = () => mockPost.mock.calls.filter(([url]) => url === '/api/reservations')
const searchCalls = () => mockPost.mock.calls.filter(([url]) => url === '/api/flights/search')

// Cada test limpia su QueryClient: una mutation deja un timer de 5 minutos que no deja salir a Jest.
const clients: ReturnType<typeof createTestQueryClient>[] = []
function newClient() {
  const client = createTestQueryClient()
  clients.push(client)
  return client
}

function renderBooking(params: Record<string, string>) {
  mockParams = Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined))
  const client = newClient()
  const utils = render(<BookFlightRoute />, { wrapper: withClient(client) })
  return { ...utils, client }
}

const fill = (label: string, value: string) => fireEvent.changeText(screen.getByLabelText(label), value)

beforeEach(() => {
  jest.clearAllMocks()
  mockedUseSession.mockReturnValue(touristSession())
})

afterEach(() => clients.splice(0).forEach((client) => client.clear()))

describe('resultados', () => {
  const request = { originIata: 'VVI', destinationIata: 'UYU', departureDate: '2026-11-20', adults: 1, childrenAges: [7], infants: 0, cabinClass: 'ECONOMY' }

  it('"Seleccionar" sólo en el vuelo de operador; la oferta de aerolínea queda DEMO y sin botón', async () => {
    mockedUseSession.mockReturnValue(guestSession())
    mockBackend(searchResponse([airlineOption(), providerOption()]))
    render(<FlightResultsScreen request={request} />, { wrapper: withClient(newClient()) })

    const select = await screen.findAllByRole('button', { name: /^Seleccionar vuelo/ })
    expect(select).toHaveLength(1)
    expect(screen.getByText('Vendido por Salar Travel')).toBeTruthy()
    expect(screen.getByText('Quedan 4 asientos')).toBeTruthy()
    // Las dos tarjetas llevan DEMO: la oferta simulada y el vuelo del operador de prueba.
    expect(screen.getAllByText('DEMO')).toHaveLength(2)
    expect(within(screen.getByTestId('inventory-banner')).getByText(/ofertas de aerolínea son simuladas y no se pueden comprar/)).toBeTruthy()
    // Sin sesión: mirar resultados no la pide.
    expect(screen.queryByText(/iniciá sesión/i)).toBeNull()
  })

  it('sólo ofertas de aerolínea: ningún "Seleccionar"', async () => {
    mockBackend(searchResponse([airlineOption(), airlineOption({ bookable: true })]))
    render(<FlightResultsScreen request={request} />, { wrapper: withClient(newClient()) })

    await screen.findByTestId('inventory-banner')
    expect(screen.queryByRole('button', { name: /Seleccionar/ })).toBeNull()
  })

  it('elegir lleva a la reserva con la búsqueda y las salidas en la URL', async () => {
    mockBackend(searchResponse([providerOption()]))
    render(<FlightResultsScreen request={request} />, { wrapper: withClient(newClient()) })

    fireEvent.press(await screen.findByRole('button', { name: /^Seleccionar vuelo/ }))
    expect(mockRouter.push).toHaveBeenCalledWith({
      pathname: '/book/flight',
      params: { o: 'VVI', d: 'UYU', dep: '2026-11-20', ad: '1', ch: '7', cab: 'ECONOMY', ids: OUT_ID },
    })
  })
})

describe('iniciar sesión al empezar la reserva', () => {
  it('sin sesión se ve el vuelo elegido y se pide entrar; al volver del login, el mismo vuelo con el formulario', async () => {
    mockedUseSession.mockReturnValue(guestSession())
    mockBackend(searchResponse([providerOption()]))
    const { rerender, client } = renderBooking(oneWayParams)

    expect(await screen.findByText('Tu vuelo de ida')).toBeTruthy()
    expect(screen.getByText('Vendido por Salar Travel')).toBeTruthy()
    expect(screen.getByTestId('login-required')).toBeTruthy()
    expect(screen.queryByTestId('passenger-form-0')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reservar' })).toBeNull()

    fireEvent.press(screen.getByRole('button', { name: 'Iniciar sesión' }))
    expect(mockRouter.push).toHaveBeenCalledWith('/(auth)/login')

    // El modal de login se cierra (router.back) y esta pantalla sigue montada con los mismos parámetros.
    mockedUseSession.mockReturnValue(touristSession())
    rerender(withClient(client)({ children: <BookFlightRoute /> }))

    expect(await screen.findByTestId('passenger-form-0')).toBeTruthy()
    expect(screen.getByTestId('passenger-form-1')).toBeTruthy()
    expect(screen.getByText('Vendido por Salar Travel')).toBeTruthy()
    expect(screen.queryByTestId('login-required')).toBeNull()
    // Volver no rehízo la búsqueda: el vuelo sale de la misma caché que los resultados.
    expect(searchCalls()).toHaveLength(1)
  })

  it('si el vuelo ya no está en la búsqueda, se dice y se ofrece volver', async () => {
    mockBackend(searchResponse([airlineOption()]))
    renderBooking(oneWayParams)

    expect(await screen.findByText('Este vuelo ya no está disponible')).toBeTruthy()
    fireEvent.press(screen.getByRole('button', { name: 'Volver a los resultados' }))
    expect(mockRouter.back).toHaveBeenCalled()
  })

  it('una URL sin salidas válidas no consulta nada', () => {
    renderBooking({ ...oneWayParams, ids: 'cualquier-cosa' })
    expect(screen.getByText('No encontramos ese vuelo')).toBeTruthy()
    expect(mockPost).not.toHaveBeenCalled()
  })
})

describe('pasajeros', () => {
  it('pide nombre, apellido y nacimiento, sin documento de identidad, y avisa que son datos ficticios', async () => {
    mockBackend(searchResponse([providerOption()]))
    renderBooking(oneWayParams)

    await screen.findByTestId('passenger-form-0')
    expect(screen.getByText('Adulto · 12 años o más')).toBeTruthy()
    expect(screen.getByText('Niño · 7 años')).toBeTruthy()
    expect(screen.getByText(/usá nombres y fechas de nacimiento ficticios. No te pedimos documento/)).toBeTruthy()
    expect(screen.queryByLabelText(/documento|pasaporte|cédula/i)).toBeNull()
  })

  it('no reserva con datos inválidos o una edad que no corresponde al tipo', async () => {
    mockBackend(searchResponse([providerOption()]))
    renderBooking(oneWayParams)
    await screen.findByTestId('passenger-form-0')

    fill('Nombre del pasajero 1', 'Ana')
    fill('Apellido del pasajero 1', 'Prueba')
    fill('Fecha de nacimiento del pasajero 1', '2016-01-01') // 10 años: no es adulto
    fill('Nombre del pasajero 2', 'Luis')
    fill('Apellido del pasajero 2', 'Prueba')
    fill('Fecha de nacimiento del pasajero 2', '2019-02-30') // no existe
    fireEvent.press(screen.getByRole('button', { name: 'Reservar' }))

    expect(screen.getByText(/12 años o más el día del vuelo/)).toBeTruthy()
    expect(screen.getByText(/formato año-mes-día/)).toBeTruthy()
    expect(screen.getByText('Revisá los datos de los pasajeros antes de reservar.')).toBeTruthy()
    expect(reservationCalls()).toHaveLength(0)
  })
})

describe('crear la reserva', () => {
  it('sólo ida: manda el contrato B1 y pasa al checkout de siempre', async () => {
    mockBackend(searchResponse([providerOption()]))
    renderBooking(oneWayParams)
    await screen.findByTestId('passenger-form-0')

    fill('Nombre del pasajero 1', 'Ana')
    fill('Apellido del pasajero 1', 'Prueba')
    fill('Fecha de nacimiento del pasajero 1', '1990-05-14')
    fill('Nombre del pasajero 2', 'Luis')
    fill('Apellido del pasajero 2', 'Prueba')
    fill('Fecha de nacimiento del pasajero 2', '2019-03-01')
    fireEvent.press(screen.getByRole('button', { name: 'Reservar' }))

    await waitFor(() => expect(reservationCalls()).toHaveLength(1))
    expect(reservationCalls()[0][1]).toEqual({
      flightDepartureIds: [OUT_ID],
      travelers: 2,
      passengers: [
        { type: 'ADULT', givenName: 'Ana', familyName: 'Prueba', bornOn: '1990-05-14' },
        { type: 'CHILD', givenName: 'Luis', familyName: 'Prueba', bornOn: '2019-03-01' },
      ],
    })
    await waitFor(() =>
      expect(mockRouter.replace).toHaveBeenCalledWith({
        pathname: '/checkout/[id]',
        params: { id: 'res-9', quotedPrice: '900', quotedCurrency: 'BOB' },
      }),
    )
  })

  it('ida y vuelta con un infante: dos salidas en orden, y el infante no cuenta como asiento', async () => {
    mockBackend(searchResponse([providerOption(true)], { returnDate: '2026-11-24' }))
    renderBooking(roundTripParams)

    expect(await screen.findByText('Tu vuelo de ida y vuelta')).toBeTruthy()
    expect(screen.getByText('Infante · menor de 2 años, en brazos')).toBeTruthy()

    fireEvent.press(screen.getByRole('button', { name: 'Completar con datos de ejemplo' }))
    fireEvent.press(screen.getByRole('button', { name: 'Reservar' }))

    await waitFor(() => expect(reservationCalls()).toHaveLength(1))
    const body = reservationCalls()[0][1]
    expect(body.flightDepartureIds).toEqual([OUT_ID, BACK_ID])
    expect(body.travelers).toBe(1)
    expect(body.passengers.map((p: { type: string }) => p.type)).toEqual(['ADULT', 'INFANT'])
    expect(body.passengers.every((p: { familyName: string }) => p.familyName === 'Prueba')).toBe(true)
  })

  it('un doble toque crea una sola reserva', async () => {
    let resolve!: (value: unknown) => void
    mockBackend(searchResponse([providerOption()]), () => new Promise((r) => (resolve = r)))
    renderBooking(oneWayParams)
    await screen.findByTestId('passenger-form-0')

    fireEvent.press(screen.getByRole('button', { name: 'Completar con datos de ejemplo' }))
    fireEvent.press(screen.getByRole('button', { name: 'Reservar' }))
    fireEvent.press(screen.getByRole('button', { name: 'Reservar' }))

    await waitFor(() => expect(reservationCalls()).toHaveLength(1))
    resolve({ data: reservationFixture({ id: 'res-9' }) })
    await waitFor(() => expect(mockRouter.replace).toHaveBeenCalledTimes(1))
  })

  it('501 FEATURE_NOT_AVAILABLE: se explica con el mensaje del backend y no se ofrece reintentar', async () => {
    mockBackend(searchResponse([providerOption()]), () =>
      Promise.reject(
        httpError(501, {
          title: 'Todavía no disponible',
          detail: 'La reserva de vuelos de operador todavía no está disponible.',
          errorCode: 'FEATURE_NOT_AVAILABLE',
        }),
      ),
    )
    renderBooking(oneWayParams)
    await screen.findByTestId('passenger-form-0')

    fireEvent.press(screen.getByRole('button', { name: 'Completar con datos de ejemplo' }))
    fireEvent.press(screen.getByRole('button', { name: 'Reservar' }))

    const panel = await screen.findByTestId('feature-not-available')
    expect(within(panel).getByText(/La reserva de vuelos de operador todavía no está disponible. No se creó ninguna reserva/)).toBeTruthy()
    expect(screen.queryByText(/Tuvimos un problema de nuestro lado/)).toBeNull()
    expect(screen.getByRole('button', { name: 'Todavía no disponible' }).props.accessibilityState.disabled).toBe(true)
    expect(mockRouter.replace).not.toHaveBeenCalled()
  })

  it('409 sin asientos: lo dice en asientos y ofrece volver a los resultados', async () => {
    mockBackend(searchResponse([providerOption()]), () => Promise.reject(httpError(409, { detail: 'Sin cupo.' })))
    renderBooking(oneWayParams)
    await screen.findByTestId('passenger-form-0')

    fireEvent.press(screen.getByRole('button', { name: 'Completar con datos de ejemplo' }))
    fireEvent.press(screen.getByRole('button', { name: 'Reservar' }))

    expect(await screen.findByText(/Ya no quedan 2 asientos libres en este vuelo/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Volver a los resultados' })).toBeTruthy()
  })

  it('caída de red: no se sabe si se creó, así que manda a revisar Mis viajes antes de reintentar', async () => {
    mockBackend(searchResponse([providerOption()]), () => Promise.reject(networkError()))
    renderBooking(oneWayParams)
    await screen.findByTestId('passenger-form-0')

    fireEvent.press(screen.getByRole('button', { name: 'Completar con datos de ejemplo' }))
    fireEvent.press(screen.getByRole('button', { name: 'Reservar' }))

    expect(await screen.findByRole('button', { name: 'Ir a Mis viajes' })).toBeTruthy()
    expect(reservationCalls()).toHaveLength(1)
  })
})
