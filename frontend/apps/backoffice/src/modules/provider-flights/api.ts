import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { providerFlightsApi } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'
import { flightErrorKind } from './flightErrors'

type ProviderFlightRequest = providerFlightsApi.ProviderFlightRequest
type ProviderFlightDepartureRequest = providerFlightsApi.ProviderFlightDepartureRequest
type RecordIssuanceRequest = providerFlightsApi.RecordIssuanceRequest

const LIST_KEY = ['myFlights'] as const
const flightKey = (id: string | undefined) => ['myFlight', id] as const
const departuresKey = (flightId: string | undefined) => ['myFlightDepartures', flightId] as const

/**
 * Un 501 o un 403 de habilitación no se reintentan: no son fallas transitorias, y reintentar sólo demora la
 * pantalla que explica qué pasa.
 */
const retryUnlessExpected = (failureCount: number, error: unknown) =>
  flightErrorKind(error) === 'other' && failureCount < 2

// ---------------------------------------------------------------- operador: vuelos

export function useMyFlights() {
  return useQuery({ queryKey: LIST_KEY, queryFn: () => providerFlightsApi.listMyFlights(httpClient), retry: retryUnlessExpected })
}

export function useMyFlight(id: string | undefined) {
  return useQuery({
    queryKey: flightKey(id),
    queryFn: () => providerFlightsApi.getMyFlight(httpClient, id!),
    enabled: !!id,
    retry: retryUnlessExpected,
  })
}

function useFlightMutation<TVars>(fn: (vars: TVars) => Promise<{ id?: string }>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: LIST_KEY })
      if (data?.id) queryClient.setQueryData(flightKey(data.id), data)
    },
  })
}

export const useCreateFlight = () =>
  useFlightMutation((body: ProviderFlightRequest) => providerFlightsApi.createFlight(httpClient, body))

export const useUpdateFlight = () =>
  useFlightMutation(({ id, body }: { id: string; body: ProviderFlightRequest }) =>
    providerFlightsApi.updateFlight(httpClient, id, body),
  )

export const usePublishFlight = () => useFlightMutation((id: string) => providerFlightsApi.publishFlight(httpClient, id))

export const useArchiveFlight = () => useFlightMutation((id: string) => providerFlightsApi.archiveFlight(httpClient, id))

// ---------------------------------------------------------------- operador: salidas

export function useDepartures(flightId: string | undefined) {
  return useQuery({
    queryKey: departuresKey(flightId),
    queryFn: () => providerFlightsApi.listDepartures(httpClient, flightId!),
    enabled: !!flightId,
    retry: retryUnlessExpected,
  })
}

function useDepartureMutation<TVars>(flightId: string, fn: (vars: TVars) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: departuresKey(flightId) })
      // "Próximas salidas" del listado depende de las salidas.
      queryClient.invalidateQueries({ queryKey: LIST_KEY })
      queryClient.invalidateQueries({ queryKey: flightKey(flightId) })
    },
  })
}

export const useCreateDeparture = (flightId: string) =>
  useDepartureMutation(flightId, (body: ProviderFlightDepartureRequest) =>
    providerFlightsApi.createDeparture(httpClient, flightId, body),
  )

export const useUpdateDeparture = (flightId: string) =>
  useDepartureMutation(flightId, ({ id, body }: { id: string; body: ProviderFlightDepartureRequest }) =>
    providerFlightsApi.updateDeparture(httpClient, id, body),
  )

export const useCloseDeparture = (flightId: string) =>
  useDepartureMutation(flightId, (id: string) => providerFlightsApi.closeDeparture(httpClient, id))

export const useDeleteDeparture = (flightId: string) =>
  useDepartureMutation(flightId, (id: string) => providerFlightsApi.deleteDeparture(httpClient, id))

// ---------------------------------------------------------------- operador: emisión

/** Refresca el detalle y el listado de reservas recibidas (las claves de provider-reservations/api.ts). */
function useIssuanceMutation<TVars extends { itemId: string }>(fn: (vars: TVars) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['companyReservation', variables.itemId] })
      queryClient.invalidateQueries({ queryKey: ['companyReservations'] })
    },
  })
}

export const useRecordIssuance = () =>
  useIssuanceMutation(({ itemId, body }: { itemId: string; body: RecordIssuanceRequest }) =>
    providerFlightsApi.recordIssuance(httpClient, itemId, body),
  )

export const useRecordIssuanceFailure = () =>
  useIssuanceMutation(({ itemId, reason }: { itemId: string; reason: string }) =>
    providerFlightsApi.recordIssuanceFailure(httpClient, itemId, { reason }),
  )
