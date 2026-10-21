import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

/**
 * Consultas de la consola de administración. Las únicas escrituras son operaciones de dominio que ya existen en
 * el backend —moderar contenido y reintentar las cancelaciones a medias—, nunca una edición manual desde una
 * tabla.
 */

export const platformKeys = {
  overview: ['admin', 'overview'] as const,
  experiences: (params: unknown) => ['admin', 'experiences', params] as const,
  packages: (params: unknown) => ['admin', 'packages', params] as const,
  reservations: (params: unknown) => ['admin', 'reservations', params] as const,
  payments: (id: string) => ['admin', 'payments', id] as const,
  cancellations: ['admin', 'cancellations'] as const,
}

export function useAdminOverview() {
  return useQuery({
    queryKey: platformKeys.overview,
    queryFn: () => adminApi.getOverview(httpClient),
    // Es una pantalla de operación: se mira para decidir qué hacer ahora, no para contemplar.
    staleTime: 30_000,
  })
}

export function useAdminExperiences(params: { status?: string; search?: string; page: number; pageSize: number }) {
  return useQuery({
    queryKey: platformKeys.experiences(params),
    queryFn: () => adminApi.listExperiences(httpClient, params),
    placeholderData: (previous) => previous,
  })
}

export function useAdminPackages(params: {
  status?: string
  search?: string
  withFlight?: boolean
  page: number
  pageSize: number
}) {
  return useQuery({
    queryKey: platformKeys.packages(params),
    queryFn: () => adminApi.listPackages(httpClient, params),
    placeholderData: (previous) => previous,
  })
}

export function useAdminReservations(params: {
  status?: string
  needsAttention?: boolean
  productType?: string
  page: number
  pageSize: number
}) {
  return useQuery({
    queryKey: platformKeys.reservations(params),
    queryFn: () => adminApi.listReservations(httpClient, params),
    placeholderData: (previous) => previous,
  })
}

export function useReservationPayments(reservationId: string) {
  return useQuery({
    queryKey: platformKeys.payments(reservationId),
    queryFn: () => adminApi.getReservationPayments(httpClient, reservationId),
    enabled: Boolean(reservationId),
  })
}

export function useUnresolvedCancellations() {
  return useQuery({
    queryKey: platformKeys.cancellations,
    queryFn: () => adminApi.listUnresolvedCancellations(httpClient, { limit: 50 }),
    staleTime: 30_000,
  })
}

export type ContentKind = 'experience' | 'package'
export type ModerationAction = 'suspend' | 'restore'

const MODERATE = {
  experience: { suspend: adminApi.suspendExperience, restore: adminApi.restoreExperience },
  package: { suspend: adminApi.suspendPackage, restore: adminApi.restorePackage },
} as const

/**
 * UC-A-07. Suspender saca el producto del catálogo público y del asistente; levantar la suspensión lo deja
 * despublicado, y volver a publicarlo queda en manos del operador.
 */
export function useModerateContent(kind: ContentKind) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: ModerationAction }) => MODERATE[kind][action](httpClient, id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin', kind === 'experience' ? 'experiences' : 'packages'] })
      void queryClient.invalidateQueries({ queryKey: platformKeys.overview })
    },
  })
}

/**
 * UC-A-10. Corre ahora la misma resolución que el proceso de fondo, que en el plan gratuito de Azure no corre
 * mientras la app duerme. Es idempotente del lado del servidor.
 */
export function useResolveCancellations() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => adminApi.resolveCancellations(httpClient),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: platformKeys.cancellations })
      void queryClient.invalidateQueries({ queryKey: ['admin', 'reservations'] })
      void queryClient.invalidateQueries({ queryKey: ['admin', 'payments'] })
      void queryClient.invalidateQueries({ queryKey: platformKeys.overview })
    },
  })
}
