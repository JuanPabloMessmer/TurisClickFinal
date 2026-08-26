import type { AxiosInstance } from 'axios'
import type { ReservationItemResponse, ReservationItemResponsePagedResult } from '../types'

/** UC-P-12 — reservas recibidas por la empresa del PROVIDER autenticado. */
export const listCompanyReservations = (http: AxiosInstance, params?: { page?: number; pageSize?: number }) =>
  http.get<ReservationItemResponsePagedResult>('/api/companies/me/reservations', { params }).then((r) => r.data)

/** UC-P-13. */
export const getCompanyReservationById = (http: AxiosInstance, id: string) =>
  http.get<ReservationItemResponse>(`/api/companies/me/reservations/${id}`).then((r) => r.data)
