import { useQuery } from '@tanstack/react-query'
import { reservationsApi } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

export function useCompanyReservations() {
  return useQuery({
    queryKey: ['companyReservations'],
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
