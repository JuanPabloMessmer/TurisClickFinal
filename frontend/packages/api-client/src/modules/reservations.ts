import type { AxiosInstance } from 'axios'
import type {
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
 * Sin idempotencia en el backend: cada llamada exitosa retiene cupo. No reintentar a ciegas.
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
 * UC-T-19 — paga una reserva PENDING_PAYMENT con el gateway simulado. Un 200 puede significar tres cosas
 * distintas: `requiresPriceAcceptance` (no se cobró nada), `paymentApproved: false` (rechazo, sigue
 * pendiente) o `paymentApproved: true` (CONFIRMED).
 */
export const payReservation = (http: AxiosInstance, id: string, body: PayReservationRequest) =>
  http.post<ReservationResponse>(`/api/reservations/${id}/pay`, body).then((r) => r.data)

/** UC-T-11 — cancela la reserva completa. Solo PENDING_PAYMENT; una CONFIRMED responde 409 `REFUND_POLICY_REQUIRED`. */
export const cancelReservation = (http: AxiosInstance, id: string) =>
  http.post<ReservationResponse>(`/api/reservations/${id}/cancel`).then((r) => r.data)
