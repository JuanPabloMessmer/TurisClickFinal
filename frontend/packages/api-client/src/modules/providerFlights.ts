import type { AxiosInstance } from 'axios'
import type { components } from '../generated/schema'

/**
 * Vuelos de operador (Workstream B). Contratos en docs/vuelos-sueltos-contratos.md.
 *
 * Implementado (B2). Errores con código que las pantallas tienen que distinguir:
 * - 403 `FLIGHT_SELLER_NOT_AUTHORIZED`: la empresa no está autorizada a vender vuelos (lo decide un ADMIN).
 *   La emisión de lo ya vendido sigue funcionando aunque se revoque.
 * - 409 al editar lo que ya se vendió (ruta, aerolínea, número, cabina, equipaje; horarios y tarifas de una
 *   salida con reservas; asientos por debajo de lo vendido; borrar una salida con reservas: se cierra).
 * - 409 al registrar la emisión de algo que no está pagado y pendiente de emitir.
 *
 * Horarios: hora local del aeropuerto, "yyyy-MM-ddTHH:mm:ss" SIN huso. Emisión: un número de pasaje por
 * pasajero (infantes incluidos), en el orden de `item.flight.passengers`; ida y vuelta son dos ítems.
 */

type Schemas = components['schemas']

export type ProviderFlightRequest = Schemas['ProviderFlightRequest']
export type ProviderFlightResponse = Schemas['ProviderFlightResponse']
export type ProviderFlightDepartureRequest = Schemas['ProviderFlightDepartureRequest']
export type ProviderFlightDepartureResponse = Schemas['ProviderFlightDepartureResponse']
export type RecordIssuanceRequest = Schemas['RecordIssuanceRequest']
export type IssuanceFailureRequest = Schemas['IssuanceFailureRequest']
export type FlightItemIssuanceResponse = Schemas['FlightItemIssuanceResponse']
export type FlightSellerAuthorizationRequest = Schemas['FlightSellerAuthorizationRequest']
export type FlightSellerAuthorizationResponse = Schemas['FlightSellerAuthorizationResponse']
export type ProviderFlightResponsePagedResult = Schemas['ProviderFlightResponsePagedResult']
export type ProviderFlightItemResponse = Schemas['ProviderFlightItemResponse']
export type ProviderFlightOfferResponse = Schemas['ProviderFlightOfferResponse']
export type ReservationPassengerRequest = Schemas['ReservationPassengerRequest']
export type ReservationPassengerResponse = Schemas['ReservationPassengerResponse']

/** Mismos valores que las constantes del backend (ProviderFlightDtos.cs). */
export const PROVIDER_FLIGHT_STATUS = { DRAFT: 'DRAFT', PUBLISHED: 'PUBLISHED', ARCHIVED: 'ARCHIVED' } as const
export const DEPARTURE_STATUS = { OPEN: 'OPEN', CLOSED: 'CLOSED', CANCELLED: 'CANCELLED' } as const
export const ISSUANCE_STATUS = { NOT_REQUIRED: 'NOT_REQUIRED', PENDING: 'PENDING', ISSUED: 'ISSUED', FAILED: 'FAILED' } as const
export const PASSENGER_TYPE = { ADULT: 'ADULT', CHILD: 'CHILD', INFANT: 'INFANT' } as const
export const OFFER_SOURCE = { AIRLINE: 'AIRLINE', PROVIDER: 'PROVIDER' } as const

/** `errorCode` de un endpoint cuyo contrato existe pero cuya funcionalidad todavía no (501). */
export const FEATURE_NOT_AVAILABLE = 'FEATURE_NOT_AVAILABLE'
/** `errorCode` (403) cuando la empresa no está autorizada a vender vuelos. Lo devuelve B2. */
export const FLIGHT_SELLER_NOT_AUTHORIZED = 'FLIGHT_SELLER_NOT_AUTHORIZED'

// ---------------------------------------------------------------- operador

export const listMyFlights = (http: AxiosInstance) =>
  http.get<ProviderFlightResponse[]>('/api/companies/me/flights').then((r) => r.data)

export const createFlight = (http: AxiosInstance, body: ProviderFlightRequest) =>
  http.post<ProviderFlightResponse>('/api/companies/me/flights', body).then((r) => r.data)

export const getMyFlight = (http: AxiosInstance, id: string) =>
  http.get<ProviderFlightResponse>(`/api/companies/me/flights/${id}`).then((r) => r.data)

export const updateFlight = (http: AxiosInstance, id: string, body: ProviderFlightRequest) =>
  http.put<ProviderFlightResponse>(`/api/companies/me/flights/${id}`, body).then((r) => r.data)

export const publishFlight = (http: AxiosInstance, id: string) =>
  http.post<ProviderFlightResponse>(`/api/companies/me/flights/${id}/publish`).then((r) => r.data)

export const archiveFlight = (http: AxiosInstance, id: string) =>
  http.post<ProviderFlightResponse>(`/api/companies/me/flights/${id}/archive`).then((r) => r.data)

export const listDepartures = (http: AxiosInstance, flightId: string) =>
  http.get<ProviderFlightDepartureResponse[]>(`/api/companies/me/flights/${flightId}/departures`).then((r) => r.data)

export const createDeparture = (http: AxiosInstance, flightId: string, body: ProviderFlightDepartureRequest) =>
  http.post<ProviderFlightDepartureResponse>(`/api/companies/me/flights/${flightId}/departures`, body).then((r) => r.data)

export const updateDeparture = (http: AxiosInstance, departureId: string, body: ProviderFlightDepartureRequest) =>
  http.put<ProviderFlightDepartureResponse>(`/api/companies/me/flight-departures/${departureId}`, body).then((r) => r.data)

export const deleteDeparture = (http: AxiosInstance, departureId: string) =>
  http.delete<void>(`/api/companies/me/flight-departures/${departureId}`)

export const closeDeparture = (http: AxiosInstance, departureId: string) =>
  http.post<ProviderFlightDepartureResponse>(`/api/companies/me/flight-departures/${departureId}/close`).then((r) => r.data)

/** Lo único que lleva un ítem a ISSUED: el operador emitió fuera de TurisClick y lo registra. */
export const recordIssuance = (http: AxiosInstance, itemId: string, body: RecordIssuanceRequest) =>
  http.post<FlightItemIssuanceResponse>(`/api/companies/me/reservation-items/${itemId}/issuance`, body).then((r) => r.data)

/** El operador no pudo emitir: el ítem se cancela con reembolso total y se liberan los asientos. */
export const recordIssuanceFailure = (http: AxiosInstance, itemId: string, body: IssuanceFailureRequest) =>
  http
    .post<FlightItemIssuanceResponse>(`/api/companies/me/reservation-items/${itemId}/issuance-failure`, body)
    .then((r) => r.data)

// ---------------------------------------------------------------- admin

export const setFlightSeller = (http: AxiosInstance, companyId: string, body: FlightSellerAuthorizationRequest) =>
  http.put<FlightSellerAuthorizationResponse>(`/api/admin/companies/${companyId}/flight-seller`, body).then((r) => r.data)

export const adminListFlights = (
  http: AxiosInstance,
  params?: { companyId?: string; status?: string; suspended?: boolean; page?: number; pageSize?: number },
) => http.get<ProviderFlightResponsePagedResult>('/api/admin/flights', { params }).then((r) => r.data)

export const suspendFlight = (http: AxiosInstance, id: string) =>
  http.post<ProviderFlightResponse>(`/api/admin/flights/${id}/suspend`).then((r) => r.data)

export const unsuspendFlight = (http: AxiosInstance, id: string) =>
  http.post<ProviderFlightResponse>(`/api/admin/flights/${id}/unsuspend`).then((r) => r.data)
