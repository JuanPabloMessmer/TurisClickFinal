import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { providerFlightsApi } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'
import { flightErrorKind } from '@/modules/provider-flights/flightErrors'

const LIST_KEY = ['adminFlights'] as const

const retryUnlessExpected = (failureCount: number, error: unknown) =>
  flightErrorKind(error) === 'other' && failureCount < 2

export function useAdminFlights(params: { status?: string; suspended?: boolean; page: number; pageSize: number }) {
  return useQuery({
    queryKey: [...LIST_KEY, params],
    queryFn: () => providerFlightsApi.adminListFlights(httpClient, params),
    placeholderData: (previous) => previous,
    retry: retryUnlessExpected,
  })
}

function useModeration(fn: (id: string) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn: fn, onSuccess: () => queryClient.invalidateQueries({ queryKey: LIST_KEY }) })
}

export const useSuspendFlight = () => useModeration((id) => providerFlightsApi.suspendFlight(httpClient, id))
export const useUnsuspendFlight = () => useModeration((id) => providerFlightsApi.unsuspendFlight(httpClient, id))

/** Autorizar o revocar la venta de vuelos de una empresa; refresca su detalle (y su estado de vendedor). */
export function useSetFlightSeller(companyId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: providerFlightsApi.FlightSellerAuthorizationRequest) =>
      providerFlightsApi.setFlightSeller(httpClient, companyId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['adminCompany', companyId] })
      queryClient.invalidateQueries({ queryKey: ['adminCompanies'] })
    },
  })
}
