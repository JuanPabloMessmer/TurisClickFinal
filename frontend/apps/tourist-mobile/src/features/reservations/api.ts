import {
  reservationsApi,
  type CreateReservationRequest,
  type PayReservationRequest,
  type ReservationResponse,
} from '@turisclick/api-client'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useRef } from 'react'
import { useSession } from '@/auth/session'
import { catalogKeys, nextPageFrom } from '@/features/catalog/queries'
import { reservationKeys } from '@/features/reservations/keys'
import { nextExpiryPoll, type ExpiryPollTracker } from '@/features/reservations/model'
import { toApiError } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'

/**
 * Queries y mutations de reservas del TOURIST. Las pantallas no hablan con axios: pantalla → hook →
 * api-client → backend. Todas las queries exigen sesión y usan keys con el id del usuario.
 */

export const TRIPS_PAGE_SIZE = 20

function useTouristId(): string | null {
  const { isAuthenticated, user } = useSession()
  return isAuthenticated && user?.id ? user.id : null
}

// ---- Queries ----

/** "Mis viajes": una sola lista paginada, en el orden real del backend (creación descendente). */
export function useMyReservations() {
  const userId = useTouristId()

  return useInfiniteQuery({
    queryKey: reservationKeys.list(userId ?? 'anonymous'),
    queryFn: ({ pageParam }) =>
      reservationsApi.listMyReservations(httpClient, { page: pageParam, pageSize: TRIPS_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: nextPageFrom,
    enabled: userId !== null,
    staleTime: 30_000,
  })
}

/**
 * Una reserva propia. `staleTime: 0` para que volver al frente la re-lea: una reserva pendiente puede
 * haber expirado o confirmarse en otro dispositivo. Si su tiempo para pagar venció y el backend todavía no
 * la expiró, se re-lee cada 30s como máximo 4 veces.
 */
export function useReservation(id: string) {
  const userId = useTouristId()
  const tracker = useRef<ExpiryPollTracker>({ polls: 0 })

  return useQuery({
    queryKey: reservationKeys.detail(userId ?? 'anonymous', id),
    queryFn: () => reservationsApi.getMyReservation(httpClient, id),
    enabled: userId !== null && Boolean(id),
    staleTime: 0,
    refetchInterval: (query) => {
      const decision = nextExpiryPoll(
        { data: query.state.data, dataUpdatedAt: query.state.dataUpdatedAt },
        Date.now(),
        tracker.current,
      )
      tracker.current = decision.tracker
      return decision.delay
    },
  })
}

/** Fuerza una re-lectura del detalle (ej. cuando la cuenta regresiva llega a 00:00). */
export function useInvalidateReservation(id: string) {
  const queryClient = useQueryClient()
  const userId = useTouristId()
  return () => {
    if (userId) void queryClient.invalidateQueries({ queryKey: reservationKeys.detail(userId, id) })
  }
}

// ---- Invalidaciones compartidas ----

function invalidateAvailabilityFor(queryClient: QueryClient, reservation: ReservationResponse) {
  for (const item of reservation.items ?? []) {
    if (item.experienceId) void queryClient.invalidateQueries({ queryKey: catalogKeys.experienceAvailability(item.experienceId) })
    if (item.packageId) void queryClient.invalidateQueries({ queryKey: catalogKeys.packageAvailability(item.packageId) })
  }
}

/** Cuando crear falla no sabemos a qué producto pertenecía la disponibilidad: se refrescan todas las visibles. */
function invalidateAllAvailability(queryClient: QueryClient) {
  void queryClient.invalidateQueries({
    predicate: (query) => query.queryKey[0] === 'catalog' && query.queryKey[query.queryKey.length - 1] === 'availability',
  })
}

// ---- Mutations ----

/**
 * UC-T-08/09. `retry: false` es deliberado: el backend no tiene idempotencia, y reintentar un POST cuya
 * respuesta se perdió podría crear una segunda reserva reteniendo cupo.
 */
export function useCreateReservation() {
  const queryClient = useQueryClient()
  const userId = useTouristId()

  return useMutation({
    mutationFn: (body: CreateReservationRequest) => reservationsApi.createReservation(httpClient, body),
    retry: false,
    onSuccess: (reservation) => {
      if (userId && reservation.id) {
        queryClient.setQueryData(reservationKeys.detail(userId, reservation.id), reservation)
        void queryClient.invalidateQueries({ queryKey: reservationKeys.list(userId) })
      }
      invalidateAvailabilityFor(queryClient, reservation)
    },
    onError: () => invalidateAllAvailability(queryClient),
  })
}

/**
 * UC-T-19. `requiresPriceAcceptance` NO es un error: es un paso del flujo, y no se cobró ni cambió nada,
 * así que no se toca la caché. Un rechazo deja la reserva PENDING_PAYMENT (se guarda igual: puede traer el
 * precio recongelado si se había aceptado). 409/410 o una caída de red → se re-lee el estado real.
 */
export function usePayReservation(id: string) {
  const queryClient = useQueryClient()
  const userId = useTouristId()

  return useMutation({
    mutationFn: (body: PayReservationRequest) => reservationsApi.payReservation(httpClient, id, body),
    retry: false,
    onSuccess: (reservation) => {
      if (reservation.requiresPriceAcceptance || !userId) return
      queryClient.setQueryData(reservationKeys.detail(userId, id), reservation)
      void queryClient.invalidateQueries({ queryKey: reservationKeys.list(userId) })
    },
    onError: (error) => {
      if (!userId) return
      const { status, isNetworkError } = toApiError(error)
      if (isNetworkError || status === 409 || status === 410) {
        void queryClient.invalidateQueries({ queryKey: reservationKeys.detail(userId, id) })
        void queryClient.invalidateQueries({ queryKey: reservationKeys.list(userId) })
      }
    },
  })
}

/** UC-T-11. Libera cupo, así que también se refresca la disponibilidad del producto. */
export function useCancelReservation(id: string) {
  const queryClient = useQueryClient()
  const userId = useTouristId()

  return useMutation({
    mutationFn: () => reservationsApi.cancelReservation(httpClient, id),
    retry: false,
    onSuccess: (reservation) => {
      if (userId) {
        queryClient.setQueryData(reservationKeys.detail(userId, id), reservation)
        void queryClient.invalidateQueries({ queryKey: reservationKeys.list(userId) })
      }
      invalidateAvailabilityFor(queryClient, reservation)
    },
    onError: (error) => {
      if (!userId) return
      const { status, isNetworkError } = toApiError(error)
      if (isNetworkError || status === 409) {
        void queryClient.invalidateQueries({ queryKey: reservationKeys.detail(userId, id) })
      }
    },
  })
}

/**
 * Borra de la caché los datos privados de UN turista (el que se fue). Solo su scope: así nunca cancela ni
 * borra las queries del turista que acaba de entrar, que pueden estar ya en vuelo. El catálogo público no
 * se toca.
 */
export async function clearPrivateQueries(queryClient: QueryClient, userId: string) {
  // Todo lo privado del turista: reservas, perfil de viaje y conversaciones con el asistente.
  for (const queryKey of [reservationKeys.scope(userId), ['preferences', userId], ['ai', userId]]) {
    await queryClient.cancelQueries({ queryKey })
    queryClient.removeQueries({ queryKey })
  }
}
