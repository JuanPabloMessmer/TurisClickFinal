import { useQuery } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

/**
 * Consultas de la consola de administración. Todas son de lectura: operar sobre una cancelación a medias es
 * una operación de dominio y no una edición manual desde una tabla.
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
