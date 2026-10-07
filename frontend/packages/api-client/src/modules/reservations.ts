import type { AxiosInstance } from 'axios'
import type {
  CancellationQuoteResponse,
  CreateReservationRequest,
  PayReservationRequest,
  ReservationItemResponse,
  ReservationItemResponsePagedResult,
  ReservationResponse,
  ReservationResponsePagedResult,
} from '../types'

// ---- PROVIDER (Backoffice) ----

/** UC-P-12 — reservas recibidas por la empresa del PROVIDER autenticado. */
export const listCompanyReservations = (http: AxiosInstance, params?: { page?: number; pageSize?: number }) =>
  http.get<ReservationItemResponsePagedResult>('/api/companies/me/reservations', { params }).then((r) => r.data)

/** UC-P-13. */
export const getCompanyReservationById = (http: AxiosInstance, id: string) =>
  http.get<ReservationItemResponse>(`/api/companies/me/reservations/${id}`).then((r) => r.data)

// ---- TOURIST (Tourist Mobile) ----

/**
 * UC-T-08/UC-T-09 — crea la reserva en PENDING_PAYMENT y retiene el cupo por 30 minutos. Exactamente uno
 * de `experienceAvailabilityId` / `packageAvailabilityId`. El precio lo congela el backend con el valor
 * vigente: el cliente nunca lo envía.
 *
 * Para un paquete con vuelo se manda además `flightQuoteId`: la opción de vuelo que la persona eligió. Ese
 * caso **sí** es idempotente —una cotización produce una sola reserva, y repetir la llamada devuelve la que
 * ya existe—; una reserva sin vuelo sigue sin clave de idempotencia, así que ahí no se reintenta a ciegas.
 */
export const createReservation = (http: AxiosInstance, body: CreateReservationRequest) =>
  http.post<ReservationResponse>('/api/reservations', body).then((r) => r.data)

/** UC-T-10 — "Mis reservas", ordenadas por fecha de creación descendente. Sin filtros de estado. */
export const listMyReservations = (http: AxiosInstance, params?: { page?: number; pageSize?: number }) =>
  http.get<ReservationResponsePagedResult>('/api/reservations/me', { params }).then((r) => r.data)

/** UC-T-10 — detalle de una reserva propia (403 si no es del turista autenticado). */
export const getMyReservation = (http: AxiosInstance, id: string) =>
  http.get<ReservationResponse>(`/api/reservations/${id}`).then((r) => r.data)

/**
 * UC-T-19 — paga una reserva PENDING_PAYMENT con el gateway simulado. Un 200 puede significar varias cosas
 * distintas: `requiresPriceAcceptance` (no se cobró nada), `requiresFlightPriceAcceptance` (cambió el
 * precio del pasaje y hace falta aceptar `flightCurrentPrice` reenviando ese mismo importe en
 * `acceptedFlightPrice`), `paymentApproved: false` (rechazo, sigue pendiente) o `paymentApproved: true`.
 *
 * Con vuelo hay un desenlace más: `flight.inProgress` significa que la emisión quedó sin resolver y el
 * backend la está reconciliando con la aerolínea. Ahí NO se reintenta el pago: se vuelve a leer la reserva.
 */
export const payReservation = (http: AxiosInstance, id: string, body: PayReservationRequest) =>
  http.post<ReservationResponse>(`/api/reservations/${id}/pay`, body).then((r) => r.data)

/**
 * UC-T-22 — qué pasaría si se cancelara: cuánto devuelve el operador por cada producto según la política que
 * la reserva congeló, y cuánto devuelve la aerolínea según lo que ella informa. **No cancela nada.**
 *
 * El presupuesto vence y pedir uno nuevo invalida el anterior. El cliente nunca manda importes: para ejecutar
 * sólo reenvía `quoteId`.
 */
export const quoteCancellation = (http: AxiosInstance, id: string) =>
  http.post<CancellationQuoteResponse>(`/api/reservations/${id}/cancellation-quote`).then((r) => r.data)

/**
 * UC-T-11 / UC-T-22 — cancela la reserva completa.
 *
 * Sin pagar todavía se cancela sin presupuesto. Ya confirmada hace falta el id del presupuesto aceptado, y el
 * resultado puede ser parcial: `cancellation.status` distingue `COMPLETED` de `REFUND_PENDING`,
 * `REQUIRES_REVIEW` y `FAILED` (no se canceló nada). La app no anuncia éxito sin leer ese estado.
 */
export const cancelReservation = (http: AxiosInstance, id: string, cancellationQuoteId?: string) =>
  http
    .post<ReservationResponse>(`/api/reservations/${id}/cancel`, { cancellationQuoteId: cancellationQuoteId ?? null })
    .then((r) => r.data)
