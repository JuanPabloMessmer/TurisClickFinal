import type { AxiosInstance } from 'axios'
import type { components } from '../generated/schema'

/**
 * Vuelos de un paquete. El cliente nunca manda un precio ni un identificador de oferta del proveedor:
 * manda desde dónde sale, para qué salida y cuántos son, y recibe el id de cotización de TurisClick.
 * La autoridad sobre el precio es del backend.
 */

export type PackageFlightRuleRequest = components['schemas']['PackageFlightRuleRequest']
export type PackageFlightRuleResponse = components['schemas']['PackageFlightRuleResponse']
export type PackageFlightQuoteRequest = components['schemas']['PackageFlightQuoteRequest']
export type PackageFlightQuoteResponse = components['schemas']['PackageFlightQuoteResponse']
export type FlightQuoteOptionResponse = components['schemas']['FlightQuoteOptionResponse']
export type FlightQuoteRevalidationResponse = components['schemas']['FlightQuoteRevalidationResponse']
export type AirportResponse = components['schemas']['AirportResponse']
export type SearchFlightsRequest = components['schemas']['SearchFlightsRequest']
export type SearchFlightsResponse = components['schemas']['SearchFlightsResponse']
export type FlightOptionResponse = components['schemas']['FlightOptionResponse']
export type FlightRoutePackageResponse = components['schemas']['FlightRoutePackageResponse']
export type AirportSearchResultResponse = components['schemas']['AirportSearchResultResponse']

/** Para qué lado del viaje se busca: el destino, por ahora, sólo admite Bolivia. */
export type AirportSearchRole = 'origin' | 'destination'

export const getPackageFlightRule = (http: AxiosInstance, packageId: string) =>
  http
    .get<PackageFlightRuleResponse | null>(`/api/packages/${packageId}/flight-rule`)
    // 204 = el paquete no tiene regla configurada. No es un error: es "todavía no incluye vuelo".
    .then((r) => (r.status === 204 ? null : r.data))

export const setPackageFlightRule = (http: AxiosInstance, packageId: string, body: PackageFlightRuleRequest) =>
  http.put<PackageFlightRuleResponse>(`/api/packages/${packageId}/flight-rule`, body).then((r) => r.data)

export const removePackageFlightRule = (http: AxiosInstance, packageId: string) =>
  http.delete<void>(`/api/packages/${packageId}/flight-rule`)

export const quotePackageFlights = (http: AxiosInstance, packageId: string, body: PackageFlightQuoteRequest) =>
  http.post<PackageFlightQuoteResponse>(`/api/packages/${packageId}/flight-quotes`, body).then((r) => r.data)

export const revalidateFlightQuote = (http: AxiosInstance, quoteId: string) =>
  http.post<FlightQuoteRevalidationResponse>(`/api/flight-quotes/${quoteId}/revalidate`).then((r) => r.data)

export const listAirports = (http: AxiosInstance, params?: { country?: string }) =>
  http.get<AirportResponse[]>('/api/airports', { params }).then((r) => r.data)

/** Autocompletado de aeropuertos del mundo (por ciudad, nombre, país o IATA). Público. Sin `q`, sugerencias. */
export const searchAirports = (
  http: AxiosInstance,
  params: { q?: string; role?: AirportSearchRole; limit?: number },
  signal?: AbortSignal,
) => http.get<AirportSearchResultResponse[]>('/api/airports/search', { params, signal }).then((r) => r.data)

/**
 * Búsqueda suelta de vuelos — el explorador de la app.
 *
 * No devuelve nada reservable a propósito: en TurisClick el pasaje se vende dentro de un paquete. Lo que sí
 * trae son los paquetes que vuelan esa ruta, que es lo único comprable.
 */
export const searchFlights = (http: AxiosInstance, body: SearchFlightsRequest) =>
  http.post<SearchFlightsResponse>('/api/flights/search', body).then((r) => r.data)
