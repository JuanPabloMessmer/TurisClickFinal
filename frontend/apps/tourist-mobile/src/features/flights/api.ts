import { flightsApi } from '@turisclick/api-client'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { httpClient } from '@/lib/httpClient'

/** El dataset de aeropuertos no cambia entre despliegues: no tiene sentido revalidarlo seguido. */
const AIRPORTS_STALE_TIME = 30 * 60_000

/**
 * Autocompletado de aeropuertos. La pantalla le pasa el texto ya "asentado" (con debounce), así que acá
 * cada clave es una consulta de verdad. Mientras llega la siguiente se siguen mostrando las sugerencias
 * anteriores: una lista que parpadea vacía en cada tecla es peor que una que tarda un instante.
 */
export function useAirportSearch(query: string, role: flightsApi.AirportSearchRole) {
  const q = query.trim()
  return useQuery({
    queryKey: ['flights', 'airports', role, q.toLowerCase()],
    queryFn: ({ signal }) => flightsApi.searchAirports(httpClient, { q: q || undefined, role, limit: 12 }, signal),
    staleTime: AIRPORTS_STALE_TIME,
    placeholderData: keepPreviousData,
  })
}

/**
 * Resultados de una búsqueda de vuelos.
 *
 * Es una query con la búsqueda como clave, no una mutation: así volver atrás y entrar de nuevo a los mismos
 * resultados no vuelve a consultar al proveedor. Lo que sí se evita es que la caché decida sola: sin
 * reintentos y sin revalidar al volver a la app, porque cada búsqueda le pide inventario a un proveedor
 * externo y eso tiene que pasar cuando la persona lo pide.
 */
export function useFlightSearchResults(request: flightsApi.SearchFlightsRequest | null) {
  return useQuery({
    queryKey: ['flights', 'search', request],
    queryFn: () => flightsApi.searchFlights(httpClient, request!),
    enabled: request !== null,
    retry: false,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  })
}
