import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import type { PackageResponse } from '@turisclick/api-client'
import { useSession } from '@/auth/session'
import { BookingScreen } from '@/features/booking/BookingScreen'
import { createTestQueryClient, ok, touristSession, withClient } from '@/test-utils'

/**
 * Elegir el vuelo durante la reserva de un paquete que lo incluye.
 *
 * Lo que se protege: que no se pueda continuar sin elegir pasaje (el backend lo rechaza, y ofrecer el botón
 * igual sería prometer algo que no va a pasar), que la cotización viaje al crear la reserva, y que cambiar
 * la cantidad de viajeros invalide la cotización — porque el precio del pasaje depende de cuántos son.
 */

const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

const QUOTE_ID = '3f6c2a9e-0000-4000-8000-000000000001'

const flightPackage = {
  id: 'p1',
  title: 'Salar de Uyuni',
  price: 480,
  currency: 'USD',
  includesFlight: true,
  flightDestinationIata: 'UYU',
  flightDestinationLabel: 'Uyuni (UYU)',
  flightOrigins: [{ iata: 'VVI', label: 'Santa Cruz de la Sierra (VVI)' }],
  flightRoundTrip: true,
} as unknown as PackageResponse

const quoteResponse = {
  packageId: 'p1',
  packageTitle: 'Salar de Uyuni',
  travelers: 1,
  packagePrice: { amount: 480, currency: 'USD' },
  originIata: 'VVI',
  originLabel: 'Santa Cruz de la Sierra (VVI)',
  destinationIata: 'UYU',
  destinationLabel: 'Uyuni (UYU)',
  outboundDate: '2026-11-20',
  inboundDate: '2026-11-24',
  cabinClass: 'ECONOMY',
  testMode: true,
  notice: null,
  options: [
    {
      quoteId: QUOTE_ID,
      flightPrice: { amount: 155, currency: 'USD' },
      combinedTotal: { amount: 635, currency: 'USD' },
      carrierName: 'Aerolínea de prueba',
      carrierIata: 'ZZ',
      expiresAt: new Date(Date.now() + 20 * 60_000).toISOString(),
      slices: [
        {
          originIata: 'VVI',
          destinationIata: 'UYU',
          durationMinutes: 85,
          stops: 0,
          segments: [
            {
              originIata: 'VVI',
              destinationIata: 'UYU',
              departingAt: '2026-11-20T08:15:00',
              arrivingAt: '2026-11-20T09:40:00',
              carrierIata: 'ZZ',
              carrierName: 'Aerolínea de prueba',
              flightNumber: '100',
              checkedBags: 1,
            },
          ],
        },
      ],
    },
  ],
}

const slots = [{ id: 'av1', date: '2026-11-20', availableSlots: 8 }]
const query = { isPending: false, isError: false, error: null, refetch: jest.fn() }

let client = createTestQueryClient()

function renderBooking() {
  const Wrapper = withClient(client)
  return render(
    <Wrapper>
      <BookingScreen
        productType="PACKAGE"
        productHref="/package/p1"
        product={{ title: 'Salar de Uyuni', price: 480, currency: 'USD' }}
        productQuery={query}
        slots={slots}
        availabilityQuery={query}
        flightPackage={flightPackage}
        today="2026-11-01"
      />
    </Wrapper>,
  )
}

const button = (name: string) => screen.getByRole('button', { name })
const isDisabled = (element: { props: { accessibilityState?: { disabled?: boolean } } }) =>
  Boolean(element.props.accessibilityState?.disabled)

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  mockedUseSession.mockReturnValue(touristSession())
})

afterEach(() => client.clear())

/** Elige la salida del 20/11, que es la única con cupo del fixture. */
function pickDeparture() {
  fireEvent.press(screen.getByLabelText(/20 de noviembre/))
}

test('no se puede continuar sin elegir el vuelo', async () => {
  renderBooking()
  pickDeparture()

  await waitFor(() => expect(isDisabled(button('Continuar'))).toBe(true))
  expect(screen.getByText('Elegí un vuelo para continuar.')).toBeTruthy()
  expect(mockPost).not.toHaveBeenCalled()
})

test('buscar vuelos cotiza con la salida y los viajeros elegidos', async () => {
  mockPost.mockResolvedValue(ok(quoteResponse))

  renderBooking()
  pickDeparture()
  fireEvent.press(screen.getByLabelText('Agregar un viajero'))
  fireEvent.press(screen.getByText('Santa Cruz de la Sierra (VVI)'))
  fireEvent.press(button('Buscar vuelos'))

  await waitFor(() => expect(mockPost).toHaveBeenCalled())
  const [url, body] = mockPost.mock.calls[0]
  expect(url).toBe('/api/packages/p1/flight-quotes')
  expect(body).toEqual({ originIata: 'VVI', packageAvailabilityId: 'av1', travelers: 2 })
})

test('al elegir un vuelo la reserva se crea con esa cotización', async () => {
  mockPost.mockResolvedValueOnce(ok(quoteResponse))

  renderBooking()
  pickDeparture()
  fireEvent.press(screen.getByText('Santa Cruz de la Sierra (VVI)'))
  fireEvent.press(button('Buscar vuelos'))

  // La primera opción queda elegida sola: es la más barata y evita un paso vacío.
  await waitFor(() => expect(isDisabled(button('Continuar'))).toBe(false))
  // El total con vuelo aparece en la opción elegida y en la barra inferior: las dos tienen que coincidir.
  expect(screen.getAllByText(/635/)).toHaveLength(2)

  mockPost.mockResolvedValueOnce(ok({ id: 'r1' }))
  fireEvent.press(button('Continuar'))

  await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(2))
  const [url, body] = mockPost.mock.calls[1]
  expect(url).toBe('/api/reservations')
  expect(body).toEqual({ packageAvailabilityId: 'av1', travelers: 1, flightQuoteId: QUOTE_ID })
})

test('cambiar la cantidad de viajeros invalida la cotización elegida', async () => {
  mockPost.mockResolvedValue(ok(quoteResponse))

  renderBooking()
  pickDeparture()
  fireEvent.press(screen.getByText('Santa Cruz de la Sierra (VVI)'))
  fireEvent.press(button('Buscar vuelos'))
  await waitFor(() => expect(isDisabled(button('Continuar'))).toBe(false))

  // El precio del pasaje depende de cuántos viajan: la cotización anterior ya no sirve.
  fireEvent.press(screen.getByLabelText('Agregar un viajero'))

  await waitFor(() => expect(isDisabled(button('Continuar'))).toBe(true))
  expect(button('Buscar vuelos')).toBeTruthy()
})

test('si el proveedor no devuelve vuelos se explica qué hacer', async () => {
  mockPost.mockResolvedValue(ok({ ...quoteResponse, options: [], notice: 'No encontramos vuelos para 20/11.' }))

  renderBooking()
  pickDeparture()
  fireEvent.press(screen.getByText('Santa Cruz de la Sierra (VVI)'))
  fireEvent.press(button('Buscar vuelos'))

  await waitFor(() => expect(screen.getByText('No encontramos vuelos para 20/11.')).toBeTruthy())
  expect(screen.getByText(/Probá con otra salida/)).toBeTruthy()
  expect(isDisabled(button('Continuar'))).toBe(true)
})
