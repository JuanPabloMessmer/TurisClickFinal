import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { Alert } from 'react-native'
import { useSession } from '@/auth/session'
import { Checkout } from '@/features/reservations/Checkout'
import {
  createTestQueryClient,
  deferred,
  httpError,
  itemFixture,
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
  dismissAll: jest.fn(),
  canDismiss: jest.fn(() => true),
  canGoBack: jest.fn(() => true),
}
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

let client = createTestQueryClient()

function renderCheckout(props: Partial<React.ComponentProps<typeof Checkout>> = {}) {
  const Wrapper = withClient(client)
  return render(
    <Wrapper>
      <Checkout id="r1" {...props} />
    </Wrapper>,
  )
}

const pending = (overrides = {}) => reservationFixture({ id: 'r1', ...overrides })
const confirmed = (overrides = {}) =>
  reservationFixture({
    id: 'r1',
    status: 'CONFIRMED',
    items: [itemFixture({ status: 'CONFIRMED' })],
    paymentApproved: true,
    ...overrides,
  })

const button = (name: string) => screen.getByRole('button', { name })
const isDisabled = (element: { props: { accessibilityState?: { disabled?: boolean } } }) =>
  Boolean(element.props.accessibilityState?.disabled)

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  mockedUseSession.mockReturnValue(touristSession())
  mockGet.mockReturnValue(ok(pending()))
})

afterEach(() => {
  client.clear()
  jest.useRealTimers()
})

describe('PENDING_PAYMENT', () => {
  it('muestra cuenta regresiva, ítems con snapshot, totales y el aviso de pago simulado', async () => {
    renderCheckout()

    await waitFor(() => expect(screen.getByText(/Tu lugar está reservado · \d\d:\d\d/)).toBeTruthy())
    expect(screen.getByText('Tour Illimani')).toBeTruthy()
    expect(screen.getByText(/2 viajeros × USD 40\.00/)).toBeTruthy()
    expect(screen.getAllByText(/USD\s+80\.00/).length).toBeGreaterThanOrEqual(2) // subtotal + total
    expect(screen.getByText('Pago de demostración')).toBeTruthy()
    expect(button('Pagar')).toBeTruthy()
    expect(button('Cancelar reserva')).toBeTruthy()
  })

  it('en desarrollo ofrece "Simular rechazo" y manda success: false', async () => {
    mockPost.mockReturnValue(ok(pending({ paymentApproved: false, paymentFailureReason: 'Pago simulado rechazado.' })))
    renderCheckout()
    await waitFor(() => expect(button('Simular rechazo')).toBeTruthy())

    fireEvent.press(button('Simular rechazo'))

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/reservations/r1/pay', { success: false, acceptPriceChanges: false }))
  })

  it('avisa si el snapshot congelado quedó distinto del precio que se vio antes de reservar', async () => {
    renderCheckout({ quotedPrice: 35, quotedCurrency: 'USD' })

    await waitFor(() => expect(screen.getByText(/El precio se actualizó al reservar: ahora es USD 40\.00/)).toBeTruthy())
  })

  it('multi-moneda: una fila por moneda, nunca un total sumado', async () => {
    mockGet.mockReturnValue(
      ok(
        pending({
          items: [
            itemFixture({ id: 'a', currency: 'USD', unitPrice: 60, travelers: 2, subtotal: 120 }),
            itemFixture({ id: 'b', experienceTitle: null, packageTitle: 'Uyuni 3 días', productType: 'PACKAGE', currency: 'BOB', unitPrice: 350, travelers: 1, subtotal: 350 }),
          ],
          totals: [
            { currency: 'USD', amount: 120 },
            { currency: 'BOB', amount: 350 },
          ],
        }),
      ),
    )
    renderCheckout()

    await waitFor(() => expect(screen.getByLabelText('Total')).toBeTruthy())
    expect(screen.getAllByText(/USD\s+120\.00/).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/BOB\s+350\.00/).length).toBeGreaterThan(0)
    expect(screen.queryByText(/470/)).toBeNull()
  })
})

describe('pago', () => {
  it('éxito: manda success: true y muestra el importe confirmado por el backend', async () => {
    mockPost.mockReturnValue(ok(confirmed({ totals: [{ currency: 'USD', amount: 80 }] })))
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())

    fireEvent.press(button('Pagar'))

    await waitFor(() => expect(screen.getByText('¡Reserva confirmada!')).toBeTruthy())
    expect(mockPost).toHaveBeenCalledWith('/api/reservations/r1/pay', { success: true, acceptPriceChanges: false })
    expect(screen.getByText('Total confirmado')).toBeTruthy()

    fireEvent.press(button('Ver mis viajes'))
    expect(mockRouter.dismissAll).toHaveBeenCalled()
    expect(mockRouter.navigate).toHaveBeenCalledWith('/trips')
  })

  it('rechazo: la reserva sigue pendiente y se puede reintentar hasta confirmar', async () => {
    mockPost
      .mockReturnValueOnce(ok(pending({ paymentApproved: false, paymentFailureReason: 'Pago simulado rechazado.' })))
      .mockReturnValueOnce(ok(confirmed()))
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())

    fireEvent.press(button('Pagar'))

    await waitFor(() => expect(screen.getByText('El pago fue rechazado')).toBeTruthy())
    expect(screen.getByText(/Pago simulado rechazado\. Tu reserva sigue pendiente/)).toBeTruthy()

    fireEvent.press(button('Reintentar pago'))

    await waitFor(() => expect(screen.getByText('¡Reserva confirmada!')).toBeTruthy())
    expect(mockPost).toHaveBeenCalledTimes(2)
  })

  it('doble toque en Pagar → una sola request', async () => {
    const inFlight = deferred<{ data: unknown }>()
    mockPost.mockReturnValue(inFlight.promise)
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())

    const pay = button('Pagar')
    fireEvent.press(pay)
    fireEvent.press(pay)

    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1))
    await act(async () => {})
    expect(mockPost).toHaveBeenCalledTimes(1)

    await act(async () => inFlight.resolve({ data: confirmed() }))
  })

  it('una caída de red no reintenta el pago: re-lee el estado real', async () => {
    mockPost.mockRejectedValue(networkError())
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())
    mockGet.mockClear()

    fireEvent.press(button('Pagar'))

    await waitFor(() => expect(screen.getByText(/No pudimos confirmar el pago por un problema de conexión/)).toBeTruthy())
    expect(mockPost).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/reservations/r1'))
  })
})

describe('cambio de precio', () => {
  const priceChanged = () =>
    pending({
      requiresPriceAcceptance: true,
      items: [
        itemFixture({ travelers: 2, unitPrice: 40, currency: 'USD', subtotal: 80, priceChanged: true, currentUnitPrice: 60, currentCurrency: 'USD' }),
      ],
      totals: [{ currency: 'USD', amount: 80 }],
    })

  it('muestra precio original, actual y el nuevo total, sin tratarlo como error', async () => {
    mockPost.mockReturnValue(ok(priceChanged()))
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())

    fireEvent.press(button('Pagar'))

    await waitFor(() => expect(screen.getByText('El precio cambió')).toBeTruthy())
    expect(screen.getByText('Antes: USD 40.00 por persona')).toBeTruthy()
    expect(screen.getByText('Ahora: USD 60.00 por persona')).toBeTruthy()
    expect(screen.getByLabelText('Nuevo total')).toBeTruthy()
    expect(screen.getAllByText(/USD\s+120\.00/).length).toBeGreaterThan(0)
    expect(screen.queryByText('El pago fue rechazado')).toBeNull()
  })

  it('"Aceptar y pagar" reenvía con acceptPriceChanges: true y muestra lo que confirmó el backend', async () => {
    mockPost
      .mockReturnValueOnce(ok(priceChanged()))
      .mockReturnValueOnce(
        ok(confirmed({ items: [itemFixture({ status: 'CONFIRMED', unitPrice: 60, subtotal: 120 })], totals: [{ currency: 'USD', amount: 120 }] })),
      )
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())
    fireEvent.press(button('Pagar'))
    await waitFor(() => expect(button('Aceptar y pagar')).toBeTruthy())

    fireEvent.press(button('Aceptar y pagar'))

    await waitFor(() => expect(screen.getByText('¡Reserva confirmada!')).toBeTruthy())
    expect(mockPost).toHaveBeenLastCalledWith('/api/reservations/r1/pay', { success: true, acceptPriceChanges: true })
    expect(screen.getAllByText(/USD\s+120\.00/).length).toBeGreaterThan(0)
  })

  it('la moneda que cambió se indica', async () => {
    mockPost.mockReturnValue(
      ok(pending({ requiresPriceAcceptance: true, items: [itemFixture({ unitPrice: 300, currency: 'BOB', priceChanged: true, currentUnitPrice: 45, currentCurrency: 'USD' })] })),
    )
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())

    fireEvent.press(button('Pagar'))

    await waitFor(() => expect(screen.getByText('La moneda también cambió.')).toBeTruthy())
    expect(screen.getByText('Antes: BOB 300.00 por persona')).toBeTruthy()
    expect(screen.getByText('Ahora: USD 45.00 por persona')).toBeTruthy()
  })
})

describe('expiración', () => {
  it('con el tiempo vencido "Pagar" está deshabilitado y no manda nada', async () => {
    mockGet.mockReturnValue(ok(pending({ expiresAt: new Date(Date.now() - 5_000).toISOString() })))
    renderCheckout()

    await waitFor(() => expect(screen.getByText('Se venció el tiempo para pagar')).toBeTruthy())
    expect(isDisabled(button('Pagar'))).toBe(true)

    fireEvent.press(button('Pagar'))
    await act(async () => {})
    expect(mockPost).not.toHaveBeenCalled()
  })

  it('al llegar a 00:00 deshabilita Pagar y re-lee la reserva, sin cambiar el estado por su cuenta', async () => {
    jest.useFakeTimers({ now: new Date('2026-10-01T12:00:00Z') })
    mockGet.mockReturnValue(ok(pending({ expiresAt: '2026-10-01T12:00:03Z' })))
    renderCheckout()

    await waitFor(() => expect(screen.getByText('Tu lugar está reservado · 00:03')).toBeTruthy())
    expect(isDisabled(button('Pagar'))).toBe(false)
    const readsBefore = mockGet.mock.calls.length

    await act(async () => {
      jest.advanceTimersByTime(4_000)
    })

    await waitFor(() => expect(isDisabled(button('Pagar'))).toBe(true))
    expect(screen.getByText('Se venció el tiempo para pagar')).toBeTruthy()
    await waitFor(() => expect(mockGet.mock.calls.length).toBeGreaterThan(readsBefore))
  })

  it('410 CON errorCode al pagar → pantalla de reserva vencida', async () => {
    mockPost.mockRejectedValue(httpError(410, { detail: 'La reserva expiró y su cupo ya fue liberado.', errorCode: 'RESERVATION_NO_LONGER_PAYABLE' }))
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())

    fireEvent.press(button('Pagar'))

    await waitFor(() => expect(screen.getByText(/el cupo retenido se liberó/)).toBeTruthy())
    expect(screen.queryByRole('button', { name: 'Pagar' })).toBeNull()
  })

  it('410 SIN errorCode al pagar → misma pantalla de reserva vencida', async () => {
    mockPost.mockRejectedValue(httpError(410, { detail: 'La reserva expiró; el cupo retenido ya no es válido para pagar.' }))
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())

    fireEvent.press(button('Pagar'))

    await waitFor(() => expect(screen.getByText(/el cupo retenido se liberó/)).toBeTruthy())
  })

  it('una reserva que el backend ya marcó EXPIRED no ofrece pagar', async () => {
    mockGet.mockReturnValue(ok(pending({ status: 'EXPIRED' })))
    renderCheckout()

    await waitFor(() => expect(screen.getByText(/el cupo retenido se liberó/)).toBeTruthy())
    expect(screen.queryByRole('button', { name: 'Pagar' })).toBeNull()
  })
})

describe('409 al pagar', () => {
  it('re-lee la reserva y, si ya estaba confirmada (ej. doble toque en otro lado), muestra el éxito', async () => {
    mockPost.mockRejectedValue(httpError(409, { detail: 'La reserva está en estado CONFIRMED; no admite pago.' }))
    renderCheckout()
    await waitFor(() => expect(button('Pagar')).toBeTruthy())
    mockGet.mockReturnValue(ok(confirmed({ paymentApproved: null })))

    fireEvent.press(button('Pagar'))

    await waitFor(() => expect(screen.getByText('¡Reserva confirmada!')).toBeTruthy())
  })
})

describe('cancelación', () => {
  it('pide confirmación, cancela y muestra la reserva cancelada', async () => {
    jest.spyOn(Alert, 'alert').mockImplementation((_title, _message, buttons) => {
      buttons?.find((b) => b.style === 'destructive')?.onPress?.()
    })
    mockPost.mockReturnValue(ok(pending({ status: 'CANCELLED', cancelledAt: new Date().toISOString(), items: [itemFixture({ status: 'CANCELLED' })] })))
    renderCheckout()
    await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())

    fireEvent.press(button('Cancelar reserva'))

    await waitFor(() => expect(screen.getByText('Cancelaste esta reserva')).toBeTruthy())
    expect(Alert.alert).toHaveBeenCalled()
    expect(mockPost).toHaveBeenCalledWith('/api/reservations/r1/cancel')
  })

  it('si la persona vuelve atrás en el diálogo no se cancela nada', async () => {
    jest.spyOn(Alert, 'alert').mockImplementation(() => {})
    renderCheckout()
    await waitFor(() => expect(button('Cancelar reserva')).toBeTruthy())

    fireEvent.press(button('Cancelar reserva'))

    expect(mockPost).not.toHaveBeenCalled()
  })
})
