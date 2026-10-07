import type { AxiosInstance } from 'axios'
import type { components } from '../generated/schema'

/**
 * Endpoints propios del administrador.
 *
 * Son distintos de los del operador a propósito: los del operador existen para garantizar que nadie vea lo
 * ajeno, y reutilizarlos con el filtro de empresa quitado debilitaría justamente eso. Acá el backend expone
 * vistas globales, pensadas para operar la plataforma.
 */

type Schemas = components['schemas']

export type AdminOverviewResponse = Schemas['AdminOverviewResponse']
export type AdminExperienceRowResponse = Schemas['AdminExperienceRowResponse']
export type AdminPackageRowResponse = Schemas['AdminPackageRowResponse']
export type AdminReservationRowResponse = Schemas['AdminReservationRowResponse']
export type CreateProviderAccountRequest = Schemas['CreateProviderAccountRequest']
export type ProviderAccountCreatedResponse = Schemas['ProviderAccountCreatedResponse']
export type ResetProviderPasswordResponse = Schemas['ResetProviderPasswordResponse']
export type CompanyUserResponse = Schemas['CompanyUserResponse']
export type ReservationPaymentsResponse = Schemas['ReservationPaymentsResponse']
export type CancellationSummaryResponse = Schemas['CancellationSummaryResponse']

type Paged<T> = { items?: T[] | null; page?: number; pageSize?: number; totalCount?: number; totalPages?: number }

/** Resumen operativo: qué hay en la plataforma, qué entró y qué está esperando a alguien. */
export const getOverview = (http: AxiosInstance) =>
  http.get<AdminOverviewResponse>('/api/admin/overview').then((r) => r.data)

export const listExperiences = (
  http: AxiosInstance,
  params?: { status?: string; search?: string; page?: number; pageSize?: number },
) => http.get<Paged<AdminExperienceRowResponse>>('/api/admin/experiences', { params }).then((r) => r.data)

export const listPackages = (
  http: AxiosInstance,
  params?: { status?: string; search?: string; withFlight?: boolean; page?: number; pageSize?: number },
) => http.get<Paged<AdminPackageRowResponse>>('/api/admin/packages', { params }).then((r) => r.data)

export const listReservations = (
  http: AxiosInstance,
  params?: { status?: string; needsAttention?: boolean; page?: number; pageSize?: number },
) => http.get<Paged<AdminReservationRowResponse>>('/api/admin/reservations', { params }).then((r) => r.data)

/** El libro de pagos de una reserva, su saldo por moneda y sus cancelaciones. */
export const getReservationPayments = (http: AxiosInstance, reservationId: string) =>
  http.get<ReservationPaymentsResponse>(`/api/admin/reservations/${reservationId}/payments`).then((r) => r.data)

/** Las cancelaciones que quedaron a medias: la cola de trabajo del administrador. */
export const listUnresolvedCancellations = (http: AxiosInstance, params?: { limit?: number }) =>
  http.get<CancellationSummaryResponse[]>('/api/admin/cancellations', { params }).then((r) => r.data)

/**
 * Da de alta una empresa con su primera cuenta de operador.
 *
 * La contraseña temporal viene en la respuesta y **no vuelve a estar disponible**: no se guarda en claro en
 * ningún lado. Si se pierde, se regenera.
 */
export const createProviderAccount = (http: AxiosInstance, body: CreateProviderAccountRequest) =>
  http.post<ProviderAccountCreatedResponse>('/api/admin/provider-accounts', body).then((r) => r.data)

/** Regenera la credencial temporal de un operador. Corta sus sesiones abiertas. */
export const resetProviderPassword = (http: AxiosInstance, userId: string) =>
  http.post<ResetProviderPasswordResponse>(`/api/admin/provider-accounts/${userId}/reset-password`).then((r) => r.data)

export const listCompanyUsers = (http: AxiosInstance, companyId: string) =>
  http.get<CompanyUserResponse[]>(`/api/admin/companies/${companyId}/users`).then((r) => r.data)
