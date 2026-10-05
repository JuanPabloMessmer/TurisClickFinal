import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { useSession } from '@/auth/session'
import { Checkout } from '@/features/reservations/Checkout'
import {
  createTestQueryClient,
  flightFixture,
  httpError,
  itemFixture,
  ok,
  reservationFixture,
  touristSession,
  withClient,
} from '@/test-utils'

/**
 * Checkout de una reserva de paquete + vuelo.
 *
 * Lo que se protege acá es el orden del flujo, que es lo que evita que alguien pague algo que no vio o que
 * un toque de más compre dos pasajes: los datos de pasajeros se validan antes de salir, un cambio de precio
 * frena el cobro y exige aceptar el importe vigente, y mientras la emisión esté sin resolver la pantalla no
 * ofrece reintentar.
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

const withFlight = (flight = {}, overrides = {}) =>
  reservationFixture({
    id: 'r1',
    items: [itemFixture({ productType: 'PACKAGE', packageId: 'p1', packageTitle: 'Salar de Uyuni', experienceId: null })],
    flight: flightFixture(flight),
    ...overrides,
  })

function renderCheckout() {
  const Wrapper = withClient(client)
  return render(
    <Wrapper>
      <Checkout id="r1" />
    </Wrapper>,
  )
}

const button = (name: string) => screen.getByRole('button', { name })

/** Completa los datos del único pasajero con valores sintéticos válidos. */
function fillTraveler(overrides: { givenName?: string } = {}) {
  fireEvent.changeText(screen.getByLabelText('Nombre'), overrides.givenName ?? 'Ana')
  fireEvent.changeText(screen.getByLabelText('Apellido'), 'Quiroga')
  fireEvent.changeText(screen.getByLabelText('Fecha de nacimiento'), '1990-05-14')
  fireEvent.changeText(screen.getByLabelText('Correo'), 'ana@example.test')
  fireEvent.changeText(screen.getByLabelText('Teléfono'), '+59170000000')
}

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  mockedUseSession.mockReturnValue(touristSession())
})

afterEach(() => client.clear())

test('muestra el vuelo, su precio aparte y el total sumado cuando la moneda coincide', async () => {
  mockGet.mockResolvedValue(ok(withFlight()))

  renderCheckout()

  await waitFor(() => expect(screen.getByText('Santa Cruz de la Sierra (VVI) → Uyuni (UYU)')).toBeTruthy())
  // "Vuelo" aparece dos veces a propósito: el bloque del pasaje y su línea en el total.
  expect(screen.getAllByText('Vuelo')).toHaveLength(2)
  // Paquete 80 + vuelo 155, misma moneda: el total existe y es la suma. Se busca el número y no un
  // formato exacto: cómo se escribe un importe lo decide Intl, no este test.
  expect(screen.getByText(/235/)).toBeTruthy()
})

test('con monedas distintas no muestra un total sumado', async () => {
  mockGet.mockResolvedValue(ok(withFlight({ price: { amount: 155, currency: 'EUR' } })))

  renderCheckout()

  await waitFor(() => expect(screen.getByText(/se cobran en monedas distintas/)).toBeTruthy())
  expect(screen.queryByText(/235/)).toBeNull()
})

test('no envía el pago si faltan los datos del pasajero', async () => {
  mockGet.mockResolvedValue(ok(withFlight()))

  renderCheckout()
  await waitFor(() => expect(button('Confirmar y pagar')).toBeTruthy())

  fireEvent.press(button('Confirmar y pagar'))

  await waitFor(() => expect(screen.getByText(/Revisá los datos de los pasajeros/)).toBeTruthy())
  expect(mockPost).not.toHaveBeenCalled()
})

test('un nombre con números se corrige antes de cobrar, no después', async () => {
  mockGet.mockResolvedValue(ok(withFlight()))

  renderCheckout()
  await waitFor(() => expect(screen.getByLabelText('Nombre')).toBeTruthy())
  fillTraveler({ givenName: 'Ana2' })

  fireEvent.press(button('Confirmar y pagar'))

  await waitFor(() => expect(screen.getByText(/sin números/)).toBeTruthy())
  expect(mockPost).not.toHaveBeenCalled()
})

test('el pago manda los datos del pasajero y confirma la reserva', async () => {
  mockGet.mockResolvedValue(ok(withFlight()))
  mockPost.mockResolvedValue(
    ok(
      withFlight(
        { status: 'CONFIRMED', bookingReference: 'ZZ4RT1', carrierName: 'Boliviana de Aviación' },
        { status: 'CONFIRMED', paymentApproved: true, items: [itemFixture({ status: 'CONFIRMED' })] },
      ),
    ),
  )

  renderCheckout()
  await waitFor(() => expect(screen.getByLabelText('Nombre')).toBeTruthy())
  fillTraveler()

  fireEvent.press(button('Confirmar y pagar'))

  await waitFor(() => expect(screen.getByText('¡Reserva confirmada!')).toBeTruthy())

  const [, body] = mockPost.mock.calls[0]
  expect(body.travelers).toHaveLength(1)
  expect(body.travelers[0]).toMatchObject({ givenName: 'Ana', familyName: 'Quiroga', bornOn: '1990-05-14' })
  // El localizador es lo que la persona necesita; el id de la orden del proveedor no sale del backend.
  expect(screen.getByText(/ZZ4RT1/)).toBeTruthy()
})

test('un cambio de precio del vuelo frena el cobro y exige aceptar el importe vigente', async () => {
  mockGet.mockResolvedValue(ok(withFlight()))
  mockPost.mockResolvedValueOnce(
    ok(
      withFlight(
        {},
        {
          requiresFlightPriceAcceptance: true,
          flightPreviousPrice: { amount: 155, currency: 'USD' },
          flightCurrentPrice: { amount: 198, currency: 'USD' },
          flightMessage: 'El precio del vuelo subió.',
        },
      ),
    ),
  )

  renderCheckout()
  await waitFor(() => expect(screen.getByLabelText('Nombre')).toBeTruthy())
  fillTraveler()
  fireEvent.press(button('Confirmar y pagar'))

  await waitFor(() => expect(screen.getByText('Cambió el precio del vuelo')).toBeTruthy())
  expect(screen.getByText(/Antes:.*155/)).toBeTruthy()
  expect(screen.getByText(/Ahora:.*198/)).toBeTruthy()
  // El botón de pagar desaparece: la única salida es aceptar el precio nuevo o volver.
  expect(screen.queryByRole('button', { name: 'Confirmar y pagar' })).toBeNull()

  mockPost.mockResolvedValueOnce(
    ok(withFlight({ status: 'CONFIRMED' }, { status: 'CONFIRMED', paymentApproved: true })),
  )
  fireEvent.press(button('Aceptar el nuevo precio y pagar'))

  await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(2))
  const [, body] = mockPost.mock.calls[1]
  // Se acepta EL IMPORTE, no un sí genérico: es lo que impide cobrar un precio que la persona no vio.
  expect(body.acceptedFlightPrice).toEqual({ amount: 198, currency: 'USD' })
})

test('mientras la emisión está sin resolver no se ofrece reintentar ni cancelar', async () => {
  mockGet.mockResolvedValue(
    ok(
      withFlight({
        status: 'RECONCILIATION_REQUIRED',
        inProgress: true,
        statusMessage: 'Estamos confirmando tu vuelo con la aerolínea.',
      }),
    ),
  )

  renderCheckout()

  await waitFor(() => expect(screen.getByText('Estamos confirmando tu vuelo con la aerolínea.')).toBeTruthy())
  // Reintentar una emisión es exactamente lo que compra dos pasajes.
  expect(screen.queryByRole('button', { name: 'Confirmar y pagar' })).toBeNull()
  expect(screen.queryByLabelText('Nombre')).toBeNull()
  expect(
    screen.getByRole('button', { name: 'Cancelar reserva' }).props.accessibilityState?.disabled,
  ).toBe(true)
})

test('si la aerolínea rechaza la emisión se explica que no se cobró nada', async () => {
  mockGet.mockResolvedValue(ok(withFlight()))
  mockPost.mockRejectedValue(httpError(409, { errorCode: 'FLIGHT_BOOKING_FAILED' }))

  renderCheckout()
  await waitFor(() => expect(screen.getByLabelText('Nombre')).toBeTruthy())
  fillTraveler()
  fireEvent.press(button('Confirmar y pagar'))

  await waitFor(() => expect(screen.getByText(/No se te cobró nada/)).toBeTruthy())
})

test('si no se pudo contactar a la aerolínea se invita a reintentar, no a rearmar la reserva', async () => {
  mockGet.mockResolvedValue(ok(withFlight()))
  mockPost.mockRejectedValue(httpError(409, { errorCode: 'FLIGHT_PROVIDER_UNREACHABLE' }))

  renderCheckout()
  await waitFor(() => expect(screen.getByLabelText('Nombre')).toBeTruthy())
  fillTraveler()
  fireEvent.press(button('Confirmar y pagar'))

  await waitFor(() => expect(screen.getByText(/Tu lugar sigue reservado/)).toBeTruthy())
})

test('una reserva sin vuelo no pide datos de pasajeros', async () => {
  mockGet.mockResolvedValue(ok(reservationFixture({ id: 'r1' })))

  renderCheckout()

  await waitFor(() => expect(button('Pagar')).toBeTruthy())
  expect(screen.queryByLabelText('Nombre')).toBeNull()
  expect(screen.queryByText('Vuelo')).toBeNull()
})
