import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { Alert } from 'react-native'
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

const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

let client = createTestQueryClient()

function renderDetail() {
  const Wrapper = withClient(client)
  return render(
    <Wrapper>
      <ReservationDetail id="r1" />
    </Wrapper>,
  )
}

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  ;(useSession as jest.MockedFunction<typeof useSession>).mockReturnValue(touristSession())
})

afterEach(() => client.clear())

it('pendiente: muestra cuenta regresiva y ofrece pagar y cancelar', async () => {
  mockGet.mockReturnValue(ok(reservationFixture({ id: 'r1' })))
  renderDetail()

  await waitFor(() => expect(screen.getByText('Pendiente de pago')).toBeTruthy())
  expect(screen.getByText(/Tu lugar está reservado/)).toBeTruthy()

  fireEvent.press(screen.getByRole('button', { name: 'Pagar' }))
  expect(mockRouter.push).toHaveBeenCalledWith('/checkout/r1')
  expect(screen.getByRole('button', { name: 'Cancelar reserva' })).toBeTruthy()
})

it('CONFIRMED no expone el CTA de cancelar ni el de pagar', async () => {
  mockGet.mockReturnValue(ok(reservationFixture({ id: 'r1', status: 'CONFIRMED', items: [itemFixture({ status: 'CONFIRMED' })] })))
  renderDetail()

  await waitFor(() => expect(screen.getByText('Confirmada')).toBeTruthy())
  expect(screen.queryByText('Cancelar reserva')).toBeNull()
  expect(screen.queryByRole('button', { name: 'Pagar' })).toBeNull()
})

it('CONFIRMED con la línea cancelada por el operador: lo dice claramente con el motivo', async () => {
  mockGet.mockReturnValue(
    ok(
      reservationFixture({
        id: 'r1',
        status: 'CONFIRMED',
        items: [itemFixture({ status: 'CANCELLED', cancelledAt: new Date().toISOString(), cancellationReason: 'Cierre del parque por mal clima' })],
      }),
    ),
  )
  renderDetail()

  await waitFor(() => expect(screen.getByText('Cancelada por el operador')).toBeTruthy())
  expect(screen.getByText('Cancelado por el operador')).toBeTruthy()
  expect(screen.getByText('Motivo del operador: Cierre del parque por mal clima')).toBeTruthy()
  expect(screen.queryByText('Cancelar reserva')).toBeNull()
})

it('multi-ítem con una línea cancelada por el operador: la reserva sigue confirmada con aviso', async () => {
  mockGet.mockReturnValue(
    ok(
      reservationFixture({
        id: 'r1',
        status: 'CONFIRMED',
        items: [
          itemFixture({ id: 'a', status: 'CONFIRMED' }),
          itemFixture({ id: 'b', experienceTitle: 'Tiwanaku', status: 'CANCELLED', cancellationReason: 'Paro de transporte' }),
        ],
      }),
    ),
  )
  renderDetail()

  await waitFor(() => expect(screen.getByText('Confirmada · 1 servicio cancelado por el operador')).toBeTruthy())
  expect(screen.getByText('Motivo del operador: Paro de transporte')).toBeTruthy()
})

it('EXPIRED explica que el cupo se liberó y no ofrece acciones', async () => {
  mockGet.mockReturnValue(ok(reservationFixture({ id: 'r1', status: 'EXPIRED', items: [itemFixture({ status: 'EXPIRED' })] })))
  renderDetail()

  await waitFor(() => expect(screen.getByText('Expirada')).toBeTruthy())
  expect(screen.getByText(/el cupo se liberó/)).toBeTruthy()
  expect(screen.queryByText('Cancelar reserva')).toBeNull()
})

it('cancela con confirmación y refleja el estado que devuelve el backend', async () => {
  jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => buttons?.find((b) => b.style === 'destructive')?.onPress?.())
  mockGet.mockReturnValue(ok(reservationFixture({ id: 'r1' })))
  mockPost.mockReturnValue(ok(reservationFixture({ id: 'r1', status: 'CANCELLED', items: [itemFixture({ status: 'CANCELLED' })] })))
  renderDetail()
  await waitFor(() => expect(screen.getByRole('button', { name: 'Cancelar reserva' })).toBeTruthy())

  fireEvent.press(screen.getByRole('button', { name: 'Cancelar reserva' }))

  await waitFor(() => expect(screen.getByText('Cancelada')).toBeTruthy())
  expect(mockPost).toHaveBeenCalledWith('/api/reservations/r1/cancel')
  expect(screen.queryByText('Cancelar reserva')).toBeNull()
})

it('si el backend responde REFUND_POLICY_REQUIRED, lo explica sin mensajes técnicos', async () => {
  jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => buttons?.find((b) => b.style === 'destructive')?.onPress?.())
  mockGet.mockReturnValue(ok(reservationFixture({ id: 'r1' })))
  mockPost.mockRejectedValue(httpError(409, { detail: 'Una reserva ya confirmada no se puede cancelar todavía…', errorCode: 'REFUND_POLICY_REQUIRED' }))
  renderDetail()
  await waitFor(() => expect(screen.getByRole('button', { name: 'Cancelar reserva' })).toBeTruthy())

  fireEvent.press(screen.getByRole('button', { name: 'Cancelar reserva' }))

  await waitFor(() => expect(screen.getByText('Las reservas confirmadas todavía no se pueden cancelar desde la app.')).toBeTruthy())
})

it('una reserva ajena (403) se muestra como no encontrada', async () => {
  mockGet.mockRejectedValue(httpError(403, { detail: 'Esta reserva no te pertenece.' }))
  renderDetail()

  await waitFor(() => expect(screen.getByText('No encontramos esta reserva')).toBeTruthy())
  expect(screen.queryByText('Esta reserva no te pertenece.')).toBeNull()
})
