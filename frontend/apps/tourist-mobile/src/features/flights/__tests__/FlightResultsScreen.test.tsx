import { fireEvent, render, screen, waitFor, within } from '@testing-library/react-native'
import type { flightsApi } from '@turisclick/api-client'
import { FlightResultsScreen } from '@/features/flights/FlightResultsScreen'
import { createTestQueryClient, httpError, ok, withClient } from '@/test-utils'

/**
 * Resultados de vuelos.
 *
 * Lo que se protege es una frontera de producto: esta pantalla **no vende pasajes**. La compra de vuelos
 * sueltos todavía no existe, así que no puede haber un botón de reservar o elegir — si aparece, estos tests
 * tienen que romperse. Y los vuelos simulados se rotulan como simulados, en la lista y en cada oferta.
 */

const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))

const request: flightsApi.SearchFlightsRequest = {
  originIata: 'MAD',
  destinationIata: 'VVI',
  departureDate: '2026-11-02',
  returnDate: '2026-11-09',
  adults: 2,
  childrenAges: [7],
  infants: 0,
  cabinClass: 'ECONOMY',
}

const segment = (from: string, to: string, departingAt: string, arrivingAt: string, checkedBags: number | null, flightNumber: string) => ({
  originIata: from,
  destinationIata: to,
  departingAt,
  arrivingAt,
  carrierIata: 'ZZ',
  carrierName: 'Fake Airways',
  flightNumber,
  checkedBags,
})

/** Directo y caro, de mañana, con valija. */
const direct = {
  price: { amount: 2400, currency: 'USD' },
  carrierName: 'Fake Airways',
  carrierIata: 'ZZ',
  slices: [
    { originIata: 'MAD', destinationIata: 'VVI', durationMinutes: 715, stops: 0, segments: [segment('MAD', 'VVI', '2026-11-02T08:15:00', '2026-11-02T20:10:00', 1, '100')] },
    { originIata: 'VVI', destinationIata: 'MAD', durationMinutes: 715, stops: 0, segments: [segment('VVI', 'MAD', '2026-11-09T08:15:00', '2026-11-09T20:10:00', 1, '110')] },
  ],
}

/** Con escala en Lima, más barato, de noche, llega al día siguiente y sin valija. */
const withStop = {
  price: { amount: 1900, currency: 'USD' },
  carrierName: 'Fake Airways',
  carrierIata: 'ZZ',
  slices: [
    {
      originIata: 'MAD',
      destinationIata: 'VVI',
      durationMinutes: 900,
      stops: 1,
      segments: [
        segment('MAD', 'LIM', '2026-11-02T20:15:00', '2026-11-03T08:00:00', 0, '103'),
        segment('LIM', 'VVI', '2026-11-03T09:35:00', '2026-11-03T11:15:00', 0, '603'),
      ],
    },
    {
      originIata: 'VVI',
      destinationIata: 'MAD',
      durationMinutes: 900,
      stops: 1,
      segments: [
        segment('VVI', 'LIM', '2026-11-09T20:15:00', '2026-11-09T22:00:00', 0, '113'),
        segment('LIM', 'MAD', '2026-11-09T23:35:00', '2026-11-10T11:15:00', 0, '613'),
      ],
    },
  ],
}

const response = (overrides: Partial<flightsApi.SearchFlightsResponse> = {}): flightsApi.SearchFlightsResponse => ({
  originIata: 'MAD',
  originLabel: 'Madrid (MAD)',
  destinationIata: 'VVI',
  destinationLabel: 'Santa Cruz de la Sierra (VVI)',
  departureDate: '2026-11-02',
  returnDate: '2026-11-09',
  passengers: 3,
  adults: 2,
  childrenAges: [7],
  infants: 0,
  cabinClass: 'ECONOMY',
  inventoryMode: 'SIMULATED',
  testMode: true,
  bookable: false,
  options: [direct, withStop],
  packages: [
    { id: 'pkg-1', title: 'Santa Cruz y las Misiones en 5 días', destinationName: 'Santa Cruz', durationDays: 5, price: { amount: 3200, currency: 'BOB' } },
  ],
  notice: null,
  ...overrides,
})

function renderScreen(req: flightsApi.SearchFlightsRequest | null = request) {
  render(<FlightResultsScreen request={req} />, { wrapper: withClient(createTestQueryClient()) })
}

const prices = () => screen.getAllByText(/^US\$|^USD/).map((node) => node.props.children as string)

beforeEach(() => {
  jest.clearAllMocks()
})

describe('resultados de vuelos', () => {
  it('manda al backend la búsqueda tal cual —pasajeros y cabina incluidos— y nunca un precio', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    await waitFor(() => expect(mockPost).toHaveBeenCalled())
    const [url, body] = mockPost.mock.calls[0]
    expect(url).toBe('/api/flights/search')
    expect(body).toEqual(request)
    expect(body).not.toHaveProperty('price')
    expect(body).not.toHaveProperty('offerId')
  })

  it('cada oferta muestra aerolínea, horarios, duración, escalas, equipaje y total', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    expect(await screen.findByText('Madrid (MAD) → Santa Cruz de la Sierra (VVI)')).toBeTruthy()
    expect(screen.getAllByText('Fake Airways').length).toBe(2)
    expect(screen.getAllByText('Directo').length).toBe(2)
    expect(screen.getAllByText('1 escala · LIM').length).toBe(2)
    expect(screen.getAllByText('11 h 55 min').length).toBe(2)
    expect(screen.getByText('1 valija despachada incluida')).toBeTruthy()
    expect(screen.getByText('Sin valija despachada')).toBeTruthy()
    // Llega al día siguiente.
    expect(screen.getAllByText(' +1').length).toBe(2)
    expect(screen.getAllByText('total · 3 pasajeros').length).toBe(2)
    // Ida y vuelta se distinguen.
    expect(screen.getAllByText('Ida').length).toBe(2)
    expect(screen.getAllByText('Vuelta').length).toBe(2)
  })

  it('los vuelos simulados se rotulan DEMO arriba y en cada oferta', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    const banner = await screen.findByTestId('inventory-banner')
    expect(within(banner).getByText('DEMO · vuelos simulados')).toBeTruthy()
    expect(within(banner).getByText(/No son vuelos reales/)).toBeTruthy()
    expect(screen.getAllByText('DEMO').length).toBe(2)
  })

  it('las ofertas del entorno de prueba de un proveedor real se rotulan como prueba', async () => {
    mockPost.mockReturnValue(ok(response({ inventoryMode: 'SANDBOX' })))
    renderScreen()

    expect(await screen.findByText('MODO PRUEBA · no se emiten pasajes')).toBeTruthy()
  })

  /** La garantía central de la pantalla. */
  it('no ofrece reservar ni elegir el vuelo, y dice por qué', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    await screen.findByTestId('inventory-banner')
    expect(screen.queryByText(/^reservar/i)).toBeNull()
    expect(screen.queryByText(/^comprar/i)).toBeNull()
    expect(screen.queryByText(/^seleccionar/i)).toBeNull()
    expect(screen.queryByText(/elegir este vuelo/i)).toBeNull()
    expect(screen.getByText(/la compra de vuelos sueltos no está disponible/i)).toBeTruthy()
  })

  it('ordena por precio, por duración y por hora de salida', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    await screen.findByTestId('inventory-banner')
    // Por defecto, el más barato primero.
    expect(prices()[0]).toContain('1.900')
    expect(prices()[1]).toContain('2.400')

    fireEvent.press(screen.getByLabelText('Más rápido'))
    expect(prices()[0]).toContain('2.400')

    fireEvent.press(screen.getByLabelText('Sale antes'))
    expect(prices()[0]).toContain('2.400')

    fireEvent.press(screen.getByLabelText('Más barato'))
    expect(prices()[0]).toContain('1.900')
  })

  it('filtra sin volver a consultar, y dice cuántos vuelos quedan', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    await screen.findByTestId('inventory-banner')
    expect(screen.getByText('2 vuelos')).toBeTruthy()

    fireEvent.press(screen.getByLabelText('Filtros'))
    fireEvent.press(screen.getByRole('tab', { name: 'Directo' }))
    fireEvent.press(screen.getByLabelText('Ver 1 vuelo'))

    expect(screen.getByText('1 de 2 vuelos')).toBeTruthy()
    expect(screen.queryByText('1 escala · LIM')).toBeNull()
    expect(screen.getByLabelText('Filtros, 1 activos')).toBeTruthy()
    expect(mockPost).toHaveBeenCalledTimes(1)
  })

  it('si los filtros vacían la lista, se ofrece quitarlos', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    await screen.findByTestId('inventory-banner')
    fireEvent.press(screen.getByLabelText('Filtros'))
    fireEvent.press(screen.getByLabelText('Madrugada (0–6)'))
    fireEvent.press(screen.getByLabelText('Ver 0 vuelos'))

    expect(screen.getByText('Ningún vuelo cumple los filtros')).toBeTruthy()
    fireEvent.press(screen.getByLabelText('Quitar filtros'))
    expect(screen.getByText('2 vuelos')).toBeTruthy()
  })

  it('lleva al paquete que cubre la ruta', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    fireEvent.press(await screen.findByLabelText('Ver Santa Cruz y las Misiones en 5 días'))
    expect(mockRouter.push).toHaveBeenCalledWith('/package/pkg-1')
  })

  it('cuando no hay vuelos lo explica en vez de quedar en blanco', async () => {
    mockPost.mockReturnValue(ok(response({ options: [], notice: 'No encontramos vuelos para el 02/11.' })))
    renderScreen()

    expect(await screen.findByText('No encontramos vuelos para el 02/11.')).toBeTruthy()
    // Y aun sin vuelos, se sabe que es una simulación.
    expect(screen.getByTestId('inventory-banner')).toBeTruthy()
  })

  it('si no hay paquetes en esa ruta lo dice, en vez de insinuar que se puede comprar el vuelo', async () => {
    mockPost.mockReturnValue(ok(response({ packages: [] })))
    renderScreen()

    expect(await screen.findByText('Todavía no hay paquetes en esta ruta')).toBeTruthy()
  })

  /**
   * Un 5xx muestra un mensaje genérico a propósito: `toApiError` nunca propaga el `detail` de un error del
   * servidor. Lo que se verifica es que la pantalla no quede en blanco y ofrezca reintentar.
   */
  it('un error del backend se muestra sin filtrar detalles internos, y se puede reintentar', async () => {
    mockPost.mockReturnValueOnce(Promise.reject(httpError(503, { detail: 'Npgsql.PostgresException' })))
    renderScreen()

    fireEvent.press(await screen.findByLabelText('Reintentar'))
    expect(screen.queryByText(/Npgsql/i)).toBeNull()
    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(2))
  })

  it('una búsqueda incompleta no consulta al backend', () => {
    renderScreen(null)

    expect(screen.getByText('Búsqueda incompleta')).toBeTruthy()
    expect(mockPost).not.toHaveBeenCalled()
  })

  it('"Editar" vuelve al buscador', async () => {
    mockPost.mockReturnValue(ok(response()))
    renderScreen()

    fireEvent.press(await screen.findByLabelText('Editar búsqueda'))
    expect(mockRouter.back).toHaveBeenCalled()
  })
})
