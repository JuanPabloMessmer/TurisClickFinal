import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { useSession } from '@/auth/session'
import { ReservationDetail } from '@/features/reservations/ReservationDetail'
import {
  createTestQueryClient,
  httpError,
  itemFixture,
  ok,
  reservationFixture,
  touristSession,
  withClient,
} from '@/test-utils'

/**
 * Cancelación con reembolso de una reserva confirmada.
 *
 * Lo que se protege: que nadie cancele sin haber visto cuánto vuelve, que el desglose se muestre **por
 * componente** —la política del operador y la de la aerolínea son cosas distintas—, que no se sume entre
 * monedas, y que un resultado parcial no se anuncie como éxito.
 */

const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))

const mockRouter = { push: jest.fn(), replace: jest.fn(), navigate: jest.fn(), back: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

let client = createTestQueryClient()

const POLICY = [
  { minDaysBefore: 30, refundPercentage: 100 },
  { minDaysBefore: 15, refundPercentage: 50 },
  { minDaysBefore: 0, refundPercentage: 0 },
]

/** Una reserva pagada de un paquete cuyo operador sí ofrece cancelación. */
const confirmed = (overrides = {}) =>
  reservationFixture({
    id: 'r1',
    status: 'CONFIRMED',
    confirmedAt: new Date().toISOString(),
    items: [
      itemFixture({
        status: 'CONFIRMED',
        productType: 'PACKAGE',
        packageId: 'p1',
        packageTitle: 'Salar de Uyuni',
        experienceId: null,
        experienceTitle: null,
        unitPrice: 1090,
        subtotal: 1090,
        travelers: 1,
        cancellationPolicy: POLICY,
      }),
    ],
    totals: [{ currency: 'USD', amount: 1090 }],
    ...overrides,
  })

const quote = (overrides = {}) => ({
  quoteId: 'q1',
  reservationId: 'r1',
  expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  lines: [
    {
      component: 'PACKAGE',
      label: 'Salar de Uyuni',
      paidAmount: 1090,
      refundAmount: 545,
      feeAmount: 545,
      currency: 'USD',
      refundPercentage: 50,
      refundKnown: true,
      explanation: 'Cancelación con 20 día(s) de anticipación: el operador reembolsa el 50%.',
    },
  ],
  refunds: [{ currency: 'USD', amount: 545 }],
  fees: [{ currency: 'USD', amount: 545 }],
  hasUnknownRefund: false,
  summary: 'Reembolso total: 545.00 USD.',
  ...overrides,
})

function renderDetail() {
  const Wrapper = withClient(client)
  return render(
    <Wrapper>
      <ReservationDetail id="r1" />
    </Wrapper>,
  )
}

const button = (name: string) => screen.getByRole('button', { name })

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  mockedUseSession.mockReturnValue(touristSession())
})

afterEach(() => client.clear())

test('una reserva confirmada con política ofrece cancelar', async () => {
  mockGet.mockResolvedValue(ok(confirmed()))

  renderDetail()

  await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())
})

test('sin política del operador no se ofrece cancelar', async () => {
  // Ofrecer el botón sería prometer algo que el backend va a rechazar.
  mockGet.mockResolvedValue(ok(confirmed({ items: [itemFixture({ status: 'CONFIRMED', cancellationPolicy: [] })] })))

  renderDetail()

  await waitFor(() => expect(screen.getByText('Confirmada')).toBeTruthy())
  expect(screen.queryByRole('button', { name: 'Cancelar reserva' })).toBeNull()
})

test('cancelar muestra el desglose antes de ejecutar nada', async () => {
  mockGet.mockResolvedValue(ok(confirmed()))
  mockPost.mockResolvedValueOnce(ok(quote()))

  renderDetail()
  await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())
  fireEvent.press(button('Cancelar reserva'))

  await waitFor(() => expect(screen.getByText('Esto es lo que pasa si cancelás')).toBeTruthy())

  // Se pidió el presupuesto y NADA se canceló todavía.
  expect(mockPost).toHaveBeenCalledWith('/api/reservations/r1/cancellation-quote')
  expect(mockPost).toHaveBeenCalledTimes(1)

  expect(screen.getByText(/el operador reembolsa el 50%/)).toBeTruthy()
  expect(screen.getByText('Cancelar no se puede deshacer. Si confirmás, se libera tu lugar y —si corresponde— se cancela el pasaje con la aerolínea.')).toBeTruthy()
})

test('confirmar manda el id del presupuesto y ningún importe', async () => {
  mockGet.mockResolvedValue(ok(confirmed()))
  mockPost.mockResolvedValueOnce(ok(quote()))

  renderDetail()
  await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())
  fireEvent.press(button('Cancelar reserva'))
  await waitFor(() => expect(button('Sí, cancelar la reserva')).toBeTruthy())

  mockPost.mockResolvedValueOnce(
    ok(
      confirmed({
        status: 'CANCELLED',
        cancellation: { status: 'COMPLETED', lines: quote().lines, flightCancelled: false },
      }),
    ),
  )
  fireEvent.press(button('Sí, cancelar la reserva'))

  await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(2))

  const [url, body] = mockPost.mock.calls[1]
  expect(url).toBe('/api/reservations/r1/cancel')
  // La autoridad sobre la plata es del servidor: el cliente sólo dice "acepto ESTE cálculo".
  expect(body).toEqual({ cancellationQuoteId: 'q1' })
  expect(JSON.stringify(body)).not.toContain('545')
})

test('con monedas distintas se muestran dos reembolsos y no una suma', async () => {
  mockGet.mockResolvedValue(ok(confirmed()))
  mockPost.mockResolvedValueOnce(
    ok(
      quote({
        lines: [
          ...quote().lines,
          {
            component: 'FLIGHT',
            label: 'Vuelo VVI → LPB',
            paidAmount: 420,
            refundAmount: 380,
            feeAmount: 40,
            currency: 'EUR',
            refundPercentage: null,
            refundKnown: true,
            explanation: 'La aerolínea devuelve 380.00 EUR y retiene el resto como cargo de cancelación.',
          },
        ],
        refunds: [
          { currency: 'EUR', amount: 380 },
          { currency: 'USD', amount: 545 },
        ],
        summary: 'Reembolso total: 380.00 EUR + 545.00 USD.',
      }),
    ),
  )

  renderDetail()
  await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())
  fireEvent.press(button('Cancelar reserva'))

  await waitFor(() => expect(screen.getByText(/Son dos monedas distintas/)).toBeTruthy())
  expect(screen.getByText(/cargo de cancelación/)).toBeTruthy()
})

test('un pasaje sin reembolso confirmado se muestra como sin confirmar', async () => {
  mockGet.mockResolvedValue(ok(confirmed()))
  mockPost.mockResolvedValueOnce(
    ok(
      quote({
        lines: [
          {
            component: 'FLIGHT',
            label: 'Vuelo VVI → LPB',
            paidAmount: 420,
            refundAmount: 0,
            feeAmount: 0,
            currency: 'USD',
            refundPercentage: null,
            refundKnown: false,
            explanation: 'La aerolínea no informó cuánto devuelve por este pasaje.',
          },
        ],
        refunds: [],
        fees: [],
        hasUnknownRefund: true,
        summary: 'La aerolínea todavía no informó cuánto devuelve.',
      }),
    ),
  )

  renderDetail()
  await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())
  fireEvent.press(button('Cancelar reserva'))

  await waitFor(() => expect(screen.getByText('Sin confirmar')).toBeTruthy())
  // "No informado" no se muestra como cero.
  expect(screen.getByText('A confirmar')).toBeTruthy()
  expect(screen.getByText('No hay reembolso')).toBeTruthy()
})

test('un reembolso pendiente no se anuncia como completado', async () => {
  mockGet.mockResolvedValue(
    ok(
      confirmed({
        status: 'CANCELLED',
        cancellation: { status: 'REFUND_PENDING', lines: quote().lines, flightCancelled: false },
      }),
    ),
  )

  renderDetail()

  await waitFor(() => expect(screen.getByText('Reembolso en proceso')).toBeTruthy())
  expect(screen.queryByText('Cancelación completada')).toBeNull()
})

test('una cancelación fallida dice que la reserva sigue vigente', async () => {
  mockGet.mockResolvedValue(
    ok(
      confirmed({
        cancellation: { status: 'FAILED', lines: [], flightCancelled: false },
      }),
    ),
  )

  renderDetail()

  await waitFor(() => expect(screen.getByText('No se pudo cancelar')).toBeTruthy())
  expect(screen.getByText(/tu reserva sigue vigente/)).toBeTruthy()
})

test('mientras la cancelación está en curso no se ofrece cancelar de nuevo', async () => {
  mockGet.mockResolvedValue(ok(confirmed({ status: 'CANCELLING' })))

  renderDetail()

  await waitFor(() => expect(screen.getByText('Cancelación en proceso')).toBeTruthy())
  expect(screen.queryByRole('button', { name: 'Cancelar reserva' })).toBeNull()
})

test('un presupuesto vencido se explica y no cancela nada', async () => {
  mockGet.mockResolvedValue(ok(confirmed()))
  mockPost.mockResolvedValueOnce(ok(quote()))

  renderDetail()
  await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())
  fireEvent.press(button('Cancelar reserva'))
  await waitFor(() => expect(button('Sí, cancelar la reserva')).toBeTruthy())

  mockPost.mockRejectedValueOnce(httpError(409, { errorCode: 'CANCELLATION_QUOTE_EXPIRED' }))
  fireEvent.press(button('Sí, cancelar la reserva'))

  // El mensaje aparece en el panel y en el aviso del pie: lo que importa es que se explique y que no se
  // haya cancelado nada.
  await waitFor(() => expect(screen.getAllByText(/El cálculo del reembolso venció/).length).toBeGreaterThan(0))
  expect(screen.queryByText('Cancelación completada')).toBeNull()
})

test('si el operador no publicó política el backend lo explica', async () => {
  mockGet.mockResolvedValue(ok(confirmed()))
  mockPost.mockRejectedValueOnce(httpError(409, { errorCode: 'CANCELLATION_POLICY_MISSING' }))

  renderDetail()
  await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())
  fireEvent.press(button('Cancelar reserva'))

  await waitFor(() => expect(screen.getByText(/no publicó una política de cancelación/)).toBeTruthy())
})
