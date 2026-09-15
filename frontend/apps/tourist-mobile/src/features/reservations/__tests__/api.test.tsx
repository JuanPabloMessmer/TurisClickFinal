import { act, renderHook, waitFor } from '@testing-library/react-native'
import { useSession } from '@/auth/session'
import {
  useCancelReservation,
  useCreateReservation,
  useMyReservations,
  usePayReservation,
  useReservation,
} from '@/features/reservations/api'
import { reservationKeys } from '@/features/reservations/keys'
import {
  createTestQueryClient,
  guestSession,
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
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>
let client = createTestQueryClient()

beforeEach(() => {
  jest.clearAllMocks()
  client = createTestQueryClient()
  mockedUseSession.mockReturnValue(touristSession('u1'))
})

afterEach(() => client.clear())

describe('queries privadas', () => {
  it('sin sesión no piden nada', async () => {
    mockedUseSession.mockReturnValue(guestSession())

    renderHook(() => useMyReservations(), { wrapper: withClient(client) })
    renderHook(() => useReservation('r1'), { wrapper: withClient(client) })
    await act(async () => {})

    expect(mockGet).not.toHaveBeenCalled()
  })

  it('las keys llevan el id del usuario', async () => {
    mockGet.mockReturnValue(ok(reservationFixture()))

    const { result } = renderHook(() => useReservation('r1'), { wrapper: withClient(client) })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(client.getQueryData(reservationKeys.detail('u1', 'r1'))).toBeTruthy()
    expect(mockGet).toHaveBeenCalledWith('/api/reservations/r1')
  })
})

describe('useCreateReservation', () => {
  it('guarda la reserva creada, invalida la lista y la disponibilidad del producto', async () => {
    const created = reservationFixture({ id: 'r-new', items: [itemFixture({ experienceId: 'e1' })] })
    mockPost.mockReturnValue(ok(created))
    const invalidate = jest.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useCreateReservation(), { wrapper: withClient(client) })
    await act(async () => {
      await result.current.mutateAsync({ experienceAvailabilityId: 's1', travelers: 2 })
    })

    expect(client.getQueryData(reservationKeys.detail('u1', 'r-new'))).toEqual(created)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: reservationKeys.list('u1') })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['catalog', 'experience', 'e1', 'availability'] })
  })

  it('NO reintenta el POST aunque el cliente tenga reintentos por defecto', async () => {
    mockPost.mockRejectedValue(networkError())

    const { result } = renderHook(() => useCreateReservation(), { wrapper: withClient(client) })
    await act(async () => {
      await result.current.mutateAsync({ experienceAvailabilityId: 's1', travelers: 1 }).catch(() => {})
    })

    // `result.current` puede ser el snapshot previo al error hasta el próximo render: se espera el estado.
    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(mockPost).toHaveBeenCalledTimes(1)
  })
})

describe('usePayReservation', () => {
  it('requiresPriceAcceptance es un paso del flujo: no toca la caché ni la lista', async () => {
    const original = reservationFixture({ id: 'r1' })
    client.setQueryData(reservationKeys.detail('u1', 'r1'), original)
    mockPost.mockReturnValue(ok(reservationFixture({ id: 'r1', requiresPriceAcceptance: true })))
    const invalidate = jest.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => usePayReservation('r1'), { wrapper: withClient(client) })
    await act(async () => {
      await result.current.mutateAsync({ success: true, acceptPriceChanges: false })
    })

    expect(client.getQueryData(reservationKeys.detail('u1', 'r1'))).toBe(original)
    expect(invalidate).not.toHaveBeenCalled()
  })

  it('aprobado: actualiza el detalle a CONFIRMED e invalida la lista', async () => {
    mockPost.mockReturnValue(ok(reservationFixture({ id: 'r1', status: 'CONFIRMED', paymentApproved: true })))
    const invalidate = jest.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => usePayReservation('r1'), { wrapper: withClient(client) })
    await act(async () => {
      await result.current.mutateAsync({ success: true, acceptPriceChanges: false })
    })

    expect(client.getQueryData<{ status: string }>(reservationKeys.detail('u1', 'r1'))?.status).toBe('CONFIRMED')
    expect(invalidate).toHaveBeenCalledWith({ queryKey: reservationKeys.list('u1') })
  })

  it('rechazado: queda PENDING_PAYMENT', async () => {
    mockPost.mockReturnValue(ok(reservationFixture({ id: 'r1', paymentApproved: false, paymentFailureReason: 'Pago simulado rechazado.' })))

    const { result } = renderHook(() => usePayReservation('r1'), { wrapper: withClient(client) })
    await act(async () => {
      await result.current.mutateAsync({ success: false, acceptPriceChanges: false })
    })

    expect(client.getQueryData<{ status: string }>(reservationKeys.detail('u1', 'r1'))?.status).toBe('PENDING_PAYMENT')
  })

  it.each([409, 410])('%s → re-lee detalle y lista, sin reintentar', async (status) => {
    mockPost.mockRejectedValue(httpError(status))
    const invalidate = jest.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => usePayReservation('r1'), { wrapper: withClient(client) })
    await act(async () => {
      await result.current.mutateAsync({ success: true, acceptPriceChanges: false }).catch(() => {})
    })

    expect(mockPost).toHaveBeenCalledTimes(1)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: reservationKeys.detail('u1', 'r1') })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: reservationKeys.list('u1') })
  })
})

describe('useCancelReservation', () => {
  it('actualiza el detalle, invalida la lista y la disponibilidad liberada', async () => {
    const cancelled = reservationFixture({
      id: 'r1',
      status: 'CANCELLED',
      items: [itemFixture({ productType: 'PACKAGE', experienceId: null, packageId: 'p1', status: 'CANCELLED' })],
    })
    mockPost.mockReturnValue(ok(cancelled))
    const invalidate = jest.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useCancelReservation('r1'), { wrapper: withClient(client) })
    await act(async () => {
      await result.current.mutateAsync()
    })

    expect(client.getQueryData(reservationKeys.detail('u1', 'r1'))).toEqual(cancelled)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: reservationKeys.list('u1') })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['catalog', 'package', 'p1', 'availability'] })
  })
})
