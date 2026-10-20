import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { reservationsApi } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

const LIST_KEY = ['companyReservations'] as const

export function useCompanyReservations() {
  return useQuery({
    queryKey: LIST_KEY,
    queryFn: () => reservationsApi.listCompanyReservations(httpClient, { page: 1, pageSize: 100 }),
  })
}

export function useCompanyReservation(id: string | undefined) {
  return useQuery({
    queryKey: ['companyReservation', id],
    queryFn: () => reservationsApi.getCompanyReservationById(httpClient, id!),
    enabled: !!id,
  })
}

/** UC-P-14 — cancelación excepcional de la propia línea. Refresca el detalle y la lista. */
export function useCancelCompanyReservation() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      reservationsApi.cancelCompanyReservation(httpClient, id, { reason }),
    onSuccess: (data, variables) => {
      queryClient.setQueryData(['companyReservation', variables.id], data)
      queryClient.invalidateQueries({ queryKey: ['companyReservation', variables.id] })
      queryClient.invalidateQueries({ queryKey: LIST_KEY })
    },
  })
}
