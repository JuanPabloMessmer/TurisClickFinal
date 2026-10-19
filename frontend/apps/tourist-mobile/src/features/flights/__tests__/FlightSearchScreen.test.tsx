import { fireEvent, render, screen, waitFor, within } from '@testing-library/react-native'
import { FlightSearchScreen } from '@/features/flights/FlightSearchScreen'
import { createTestQueryClient, httpError, ok, withClient } from '@/test-utils'

/**
 * El explorador de vuelos.
 *
 * Lo que se protege acá es una frontera de producto, no un detalle visual: esta pantalla **no vende
 * pasajes**. TurisClick vende el vuelo dentro de un paquete, así que acá no puede haber un botón de
 * reservar ni nada que se le parezca — si alguna vez aparece, estos tests tienen que romperse.
 *
 * Lo demás que se fija: que las ofertas de prueba se rotulen como tales, que "no hay vuelos" se explique
 * en vez de quedar en blanco, y que desde una ruta se llegue a los paquetes que sí se pueden comprar.
 */

const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))

const airports = [
  { iata: 'VVI', label: 'Santa Cruz de la Sierra (VVI)' },
  { iata: 'LPB', label: 'La Paz (LPB)' },
]

const resultado = {
  originIata: 'VVI',
  originLabel: 'Santa Cruz de la Sierra (VVI)',
  destinationIata: 'LPB',
  destinationLabel: 'La Paz (LPB)',
  departureDate: '2026-12-01',
  passengers: 2,
  cabinClass: 'ECONOMY',
  testMode: true,
  options: [
    {
      price: { amount: 240, currency: 'USD' },
      carrierName: 'Boliviana de Aviación',
      carrierIata: 'OB',
      slices: [
        {
          originIata: 'VVI',
          destinationIata: 'LPB',
          durationMinutes: 85,
          stops: 0,
          segments: [
            {
              originIata: 'VVI',
              destinationIata: 'LPB',
              departingAt: '2026-12-01T08:15:00',
              arrivingAt: '2026-12-01T09:40:00',
              carrierIata: 'OB',
              carrierName: 'Boliviana de Aviación',
              flightNumber: '712',
            },
          ],
        },
      ],
    },
  ],
  packages: [
    {
      id: 'pkg-1',
      title: 'La Paz y el Salar en 4 días',
      destinationName: 'La Paz',
      durationDays: 4,
      price: { amount: 2400, currency: 'BOB' },
    },
  ],
  notice: null,
}

function renderScreen() {
  render(<FlightSearchScreen />, { wrapper: withClient(createTestQueryClient()) })
}

/** Completa el formulario mínimo y dispara la búsqueda. */
async function buscar() {
  // Primero que lleguen los aeropuertos: hasta entonces los selectores muestran esqueletos.
  await waitFor(() => expect(screen.getAllByLabelText('Santa Cruz de la Sierra (VVI)').length).toBeGreaterThan(0))
  // Los dos selectores listan los mismos aeropuertos, así que se busca dentro de cada uno.
  fireEvent.press(within(screen.getByTestId('picker-origen')).getByLabelText('Santa Cruz de la Sierra (VVI)'))
  fireEvent.press(within(screen.getByTestId('picker-destino')).getByLabelText('La Paz (LPB)'))
  fireEvent.press(screen.getByLabelText('Buscar vuelos'))
}

beforeEach(() => {
  jest.clearAllMocks()
  mockGet.mockResolvedValue(ok(airports))
})

describe('explorador de vuelos', () => {
  it('muestra las ofertas con aerolínea, precio, horarios y escalas', async () => {
    mockPost.mockResolvedValue(ok(resultado))
    renderScreen()

    await buscar()

    await waitFor(() => expect(screen.getByText('Boliviana de Aviación')).toBeTruthy())
    expect(screen.getByText(/VVI → LPB/)).toBeTruthy()
    expect(screen.getByText(/USD\s?240,00/)).toBeTruthy()
    // Directo: cero escalas.
    expect(screen.getByText(/directo/i)).toBeTruthy()
  })

  it('manda al backend lo que se eligió, y nunca un precio', async () => {
    mockPost.mockResolvedValue(ok(resultado))
    renderScreen()

    await buscar()

    await waitFor(() => expect(mockPost).toHaveBeenCalled())
    const [url, body] = mockPost.mock.calls[0]
    expect(url).toBe('/api/flights/search')
    expect(body).toMatchObject({ originIata: 'VVI', destinationIata: 'LPB', passengers: 1 })
    expect(body).not.toHaveProperty('price')
    expect(body).not.toHaveProperty('offerId')
  })

  it('rotula las ofertas de prueba como tales', async () => {
    mockPost.mockResolvedValue(ok(resultado))
    renderScreen()

    await buscar()

    await waitFor(() => expect(screen.getByText('Vuelos de prueba')).toBeTruthy())
    expect(screen.getByText(/no se emite ningún pasaje desde acá/i)).toBeTruthy()
  })

  /** La garantía central de la pantalla. */
  it('no ofrece reservar el vuelo: eso se hace en el paquete', async () => {
    mockPost.mockResolvedValue(ok(resultado))
    renderScreen()

    await buscar()

    await waitFor(() => expect(screen.getByText('Boliviana de Aviación')).toBeTruthy())
    expect(screen.queryByText(/^reservar/i)).toBeNull()
    expect(screen.queryByText(/^comprar/i)).toBeNull()
    expect(screen.queryByText(/elegir este vuelo/i)).toBeNull()
    expect(screen.getByText(/TurisClick no vende pasajes sueltos/i)).toBeTruthy()
  })

  it('lleva al paquete que cubre la ruta', async () => {
    mockPost.mockResolvedValue(ok(resultado))
    renderScreen()

    await buscar()

    await waitFor(() => expect(screen.getByText('La Paz y el Salar en 4 días')).toBeTruthy())
    fireEvent.press(screen.getByLabelText('Ver La Paz y el Salar en 4 días'))

    expect(mockRouter.push).toHaveBeenCalledWith('/package/pkg-1')
  })

  it('cuando no hay vuelos lo explica en vez de quedar en blanco', async () => {
    mockPost.mockResolvedValue(ok({ ...resultado, options: [], notice: 'No encontramos vuelos para el 01/12.' }))
    renderScreen()

    await buscar()

    await waitFor(() => expect(screen.getByText('No encontramos vuelos para el 01/12.')).toBeTruthy())
  })

  it('si no hay paquetes en esa ruta lo dice, en vez de insinuar que se puede comprar el vuelo', async () => {
    mockPost.mockResolvedValue(ok({ ...resultado, packages: [] }))
    renderScreen()

    await buscar()

    await waitFor(() => expect(screen.getByText('Todavía no hay paquetes en esta ruta')).toBeTruthy())
  })

  /**
   * Un 5xx muestra un mensaje genérico a propósito: `toApiError` nunca propaga el `detail` de un error del
   * servidor porque ahí viaja el mensaje crudo de la excepción. Lo que sí se verifica es que la pantalla
   * no quede en blanco y ofrezca reintentar.
   */
  it('un error del backend se muestra sin filtrar detalles internos, y se puede reintentar', async () => {
    mockPost.mockRejectedValue(httpError(503, { detail: 'Npgsql.PostgresException en flight_quotes' }))
    renderScreen()

    await buscar()

    await waitFor(() => expect(screen.getByLabelText('Reintentar')).toBeTruthy())
    expect(screen.queryByText(/Npgsql/i)).toBeNull()
  })
})
