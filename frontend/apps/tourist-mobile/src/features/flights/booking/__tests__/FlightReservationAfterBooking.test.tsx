import { fireEvent, render, screen, waitFor, within } from '@testing-library/react-native'
import TripsScreen from '@/../app/(tabs)/trips'
import { useSession } from '@/auth/session'
import { Checkout } from '@/features/reservations/Checkout'
import { describePayFailure } from '@/features/reservations/errors'
import { ReservationDetail } from '@/features/reservations/ReservationDetail'
import { toApiError } from '@/lib/errors'
import {
  createTestQueryClient,
  httpError,
  itemFixture,
  ok,
  pagedFixture,
  reservationFixture,
  touristSession,
  withClient,
} from '@/test-utils'
import { flightReservation } from '@/test-utils/providerFlights'

/**
 * Después de reservar un vuelo de operador, con respuestas simuladas con la forma de B2: el pago es el
 * checkout simulado de siempre, y lo que dice la app sobre el pasaje sigue a `issuanceStatus`:
 * - pagar NO es emitir: "Emisión pendiente" hasta que el operador registra la emisión;
 * - "Pasaje emitido" sólo con ISSUED;
 * - emisión fallida: dicho con el reembolso total;
 * - DEMO siempre a la vista.
 * Y los paquetes con vuelo (FlightBookingResponse) se siguen viendo como antes.
 */

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
  dismissAll: jest.fn(),
  canDismiss: jest.fn(() => true),
  canGoBack: jest.fn(() => true),
}
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>
let client = createTestQueryClient()

function renderWithClient(ui: React.ReactElement) {
  const Wrapper = withClient(client)
  return render(<Wrapper>{ui}</Wrapper>)
}

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  mockedUseSession.mockReturnValue(touristSession())
})

afterEach(() => client.clear())

describe('checkout: el pago simulado de siempre', () => {
  it('muestra el vuelo con DEMO, no pide datos de pasajeros de nuevo y paga con el gateway simulado', async () => {
    mockGet.mockReturnValue(ok(flightReservation('PENDING_PAYMENT', 'NOT_REQUIRED', { roundTrip: true })))
    mockPost.mockReturnValue(ok(flightReservation('CONFIRMED', 'PENDING', { roundTrip: true })))
    renderWithClient(<Checkout id="r1" />)

    const cards = await screen.findAllByTestId('provider-flight-item')
    expect(cards).toHaveLength(2)
    expect(within(cards[0]).getByText('Vuelo · Ida')).toBeTruthy()
    expect(within(cards[1]).getByText('Vuelo · Vuelta')).toBeTruthy()
    expect(within(cards[0]).getByText('DEMO')).toBeTruthy()
    expect(within(cards[0]).getByText('Pendiente de pago')).toBeTruthy()
    expect(within(cards[0]).getByText('Ana Prueba · Adulto')).toBeTruthy()
    expect(screen.getByText('Pago de demostración')).toBeTruthy()
    // Los pasajeros se cargaron al reservar: el checkout no pide el formulario de pasajeros de aerolínea.
    expect(screen.queryByLabelText('Correo')).toBeNull()

    fireEvent.press(screen.getByRole('button', { name: 'Pagar' }))

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/reservations/r1/pay', { success: true, acceptPriceChanges: false }))
    expect(await screen.findByText('¡Pago confirmado!')).toBeTruthy()
    expect(screen.getByText(/emisión pendiente por Salar Travel. Te avisamos en Mis viajes/)).toBeTruthy()
    // Y cada tramo lo repite en su tarjeta.
    expect(screen.getAllByText('Emisión pendiente')).toHaveLength(2)
    expect(screen.queryByText(/Pasaje emitido/)).toBeNull()
    expect(screen.queryByText(/pasaje quedaron asegurados/)).toBeNull()
  })

  it('409 RESERVATION_NO_LONGER_PAYABLE: dice que no queda nada por cobrar y deja de ofrecer pagar', async () => {
    mockGet.mockReturnValue(ok(flightReservation('PENDING_PAYMENT', 'NOT_REQUIRED')))
    mockPost.mockImplementation(() =>
      Promise.reject(
        httpError(409, {
          detail: 'Esta reserva ya no tiene servicios para pagar: el operador los canceló.',
          errorCode: 'RESERVATION_NO_LONGER_PAYABLE',
        }),
      ),
    )
    renderWithClient(<Checkout id="r1" />)

    fireEvent.press(await screen.findByRole('button', { name: 'Pagar' }))

    expect(await screen.findByText('Esta reserva ya no se puede pagar')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Pagar' })).toBeNull()
    expect(mockPost).toHaveBeenCalledTimes(1)
  })
})

describe('detalle de la reserva: estado del pasaje', () => {
  it('pagada sin emitir: "Emisión pendiente", nunca "Pasaje emitido"', async () => {
    mockGet.mockReturnValue(ok(flightReservation('CONFIRMED', 'PENDING')))
    renderWithClient(<ReservationDetail id="r1" />)

    expect(await screen.findByText('Pagada · emisión pendiente')).toBeTruthy()
    const status = screen.getByTestId('issuance-status')
    expect(within(status).getByText('Emisión pendiente')).toBeTruthy()
    expect(within(status).getByText(/Pago confirmado · emisión pendiente por Salar Travel/)).toBeTruthy()
    expect(screen.queryByText(/Pasaje emitido/)).toBeNull()
    expect(screen.getByText('DEMO')).toBeTruthy()
  })

  it('emitido por el operador: "Pasaje emitido", cada pasaje con su pasajero y el aviso de que es una demo', async () => {
    mockGet.mockReturnValue(
      ok(flightReservation('CONFIRMED', 'ISSUED', { ticketNumbers: ['9301234567890', '9301234567891'], bookingReference: 'QX7P2L' })),
    )
    renderWithClient(<ReservationDetail id="r1" />)

    const status = await screen.findByTestId('issuance-status')
    expect(within(status).getByText('Pasaje emitido')).toBeTruthy()
    expect(within(status).getByText('Pasaje emitido por Salar Travel.')).toBeTruthy()
    expect(within(status).getByText('Ana Prueba · Pasaje 9301234567890')).toBeTruthy()
    expect(within(status).getByText('Luis Prueba · Pasaje 9301234567891')).toBeTruthy()
    expect(within(status).getByText('Localizador QX7P2L')).toBeTruthy()
    expect(within(status).getByText(/no es un pasaje real/)).toBeTruthy()
  })

  it('emisión fallida: lo dice con el reembolso total (simulado)', async () => {
    mockGet.mockReturnValue(ok(flightReservation('CONFIRMED', 'FAILED')))
    renderWithClient(<ReservationDetail id="r1" />)

    const status = await screen.findByTestId('issuance-status')
    expect(within(status).getByText('Emisión fallida · reembolso total')).toBeTruthy()
    expect(within(status).getByText(/no pudo emitir el pasaje/)).toBeTruthy()
    expect(within(status).getByText(/Reembolso total: Bs.*900.*\(simulado\)/)).toBeTruthy()
    expect(screen.queryByText(/Pasaje emitido/)).toBeNull()
  })
})

describe('Mis viajes', () => {
  it('cada vuelo con su estado de pasaje y DEMO; ida y vuelta es una sola compra', async () => {
    mockGet.mockReturnValue(
      ok(pagedFixture([flightReservation('CONFIRMED', 'ISSUED', { roundTrip: true }, { id: 'r-flight' })])),
    )
    renderWithClient(<TripsScreen />)

    expect(await screen.findByText('Vuelo VVI → UYU · ida y vuelta')).toBeTruthy()
    expect(screen.queryByText(/\+1 más/)).toBeNull()
    const summary = screen.getByTestId('trip-flight-summary')
    expect(within(summary).getAllByText('Pasaje emitido')).toHaveLength(2)
    expect(within(summary).getAllByText('DEMO')).toHaveLength(2)
    expect(within(summary).getByText(/^Ida /)).toBeTruthy()
    expect(within(summary).getByText(/^Vuelta /)).toBeTruthy()
  })

  it('las reservas de paquete con vuelo se siguen viendo como antes', async () => {
    mockGet.mockReturnValue(
      ok(
        pagedFixture([
          reservationFixture({
            id: 'r-pkg',
            status: 'CONFIRMED',
            items: [itemFixture({ productType: 'PACKAGE', packageTitle: 'Salar en 3 días', experienceTitle: null, status: 'CONFIRMED' })],
            flight: {
              status: 'CONFIRMED',
              originIata: 'VVI',
              destinationIata: 'UYU',
              travelers: 2,
              price: { amount: 155, currency: 'USD' },
              inProgress: false,
            },
          }),
        ]),
      ),
    )
    renderWithClient(<TripsScreen />)

    expect(await screen.findByText('Salar en 3 días')).toBeTruthy()
    expect(screen.getByText('Vuelo VVI–UYU')).toBeTruthy()
    expect(screen.getByText('Confirmada')).toBeTruthy()
    expect(screen.queryByTestId('trip-flight-summary')).toBeNull()
  })
})

describe('errores con código', () => {
  it('un 501 FEATURE_NOT_AVAILABLE conserva su mensaje; cualquier otro 5xx sigue siendo genérico', () => {
    const notAvailable = toApiError(httpError(501, { detail: 'Todavía no.', errorCode: 'FEATURE_NOT_AVAILABLE' }))
    expect(notAvailable).toMatchObject({ status: 501, code: 'FEATURE_NOT_AVAILABLE', message: 'Todavía no.' })

    const other501 = toApiError(httpError(501, { detail: 'stack de EF' }))
    expect(other501.message).toBe('Tuvimos un problema de nuestro lado. Intentá de nuevo en un momento.')
    expect(other501.code).toBeUndefined()
  })

  it('RESERVATION_NO_LONGER_PAYABLE como 410 sigue siendo "expiró"; como 409, "ya no se puede pagar"', () => {
    expect(describePayFailure(httpError(410, { errorCode: 'RESERVATION_NO_LONGER_PAYABLE' })).kind).toBe('EXPIRED')
    expect(describePayFailure(httpError(409, { errorCode: 'RESERVATION_NO_LONGER_PAYABLE' })).kind).toBe('NO_LONGER_PAYABLE')
    expect(describePayFailure(httpError(409, {})).kind).toBe('STALE')
  })
})
