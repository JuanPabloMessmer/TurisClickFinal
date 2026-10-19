import { flightsApi } from '@turisclick/api-client'
import { useMutation, useQuery } from '@tanstack/react-query'
import { httpClient } from '@/lib/httpClient'

/**
 * Búsqueda de vuelos del explorador.
 *
 * Es una `mutation` y no una `query` aunque "sólo lea": cada búsqueda le pide inventario a un proveedor
 * externo, así que tiene que dispararse cuando la persona lo pide y no cuando a la caché le parece. Es el
 * mismo criterio que usa el presupuesto de cancelación.
 *
 * `retry: false` por la misma razón: reintentar solo multiplica las consultas al proveedor sin que nadie
 * lo haya pedido.
 */
export function useFlightSearch() {
  return useMutation({
    mutationFn: (body: flightsApi.SearchFlightsRequest) => flightsApi.searchFlights(httpClient, body),
    retry: false,
  })
}

/** El catálogo de aeropuertos casi no cambia: no tiene sentido revalidarlo seguido. */
const AIRPORTS_STALE_TIME = 30 * 60_000

export function useAirports() {
  return useQuery({
    queryKey: ['flights', 'airports'],
    queryFn: () => flightsApi.listAirports(httpClient),
    staleTime: AIRPORTS_STALE_TIME,
  })
}
