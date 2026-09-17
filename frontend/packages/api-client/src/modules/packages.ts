import type { AxiosInstance } from 'axios'
import type {
  BulkCreatePackageAvailabilityRequest,
  BulkPackageAvailabilityResponse,
  UpdateAvailabilityRequest,
  CreatePackageAvailabilityRequest,
  CreatePackageRequest,
  PackageAvailabilityResponse,
  PackageResponse,
  PackageSummaryResponsePagedResult,
  UpdatePackageRequest,
} from '../types'

/** UC-P-07 — exclusivo PROVIDER (crea para su propia empresa, resuelta server-side). */
export const createPackage = (http: AxiosInstance, body: CreatePackageRequest) =>
  http.post<PackageResponse>('/api/packages', body).then((r) => r.data)

/** UC-P-08. */
export const updatePackage = (http: AxiosInstance, id: string, body: UpdatePackageRequest) =>
  http.put<PackageResponse>(`/api/packages/${id}`, body).then((r) => r.data)

/** UC-P-09. */
export const publishPackage = (http: AxiosInstance, id: string) =>
  http.post<PackageResponse>(`/api/packages/${id}/publish`).then((r) => r.data)

export const unpublishPackage = (http: AxiosInstance, id: string) =>
  http.post<PackageResponse>(`/api/packages/${id}/unpublish`).then((r) => r.data)

/** "Mis paquetes" — cualquier estado, exclusivo PROVIDER dueño. */
export const listMyPackages = (http: AxiosInstance, params?: { page?: number; pageSize?: number }) =>
  http.get<PackageSummaryResponsePagedResult>('/api/packages/mine', { params }).then((r) => r.data)

export const getMyPackageById = (http: AxiosInstance, id: string) =>
  http.get<PackageResponse>(`/api/packages/mine/${id}`).then((r) => r.data)

/** UC-P-11 — una salida por request. */
export const createPackageAvailability = (http: AxiosInstance, packageId: string, body: CreatePackageAvailabilityRequest) =>
  http.post<PackageAvailabilityResponse>(`/api/packages/${packageId}/availability`, body).then((r) => r.data)

/** Vista de gestión del PROVIDER dueño — todas las salidas, cualquier fecha/estado. */
export const listOwnedPackageAvailability = (http: AxiosInstance, packageId: string) =>
  http.get<PackageAvailabilityResponse[]>(`/api/packages/mine/${packageId}/availability`).then((r) => r.data)

/** Calendario: genera salidas concretas de un rango según un patrón semanal. */
export const bulkCreatePackageAvailability = (http: AxiosInstance, packageId: string, body: BulkCreatePackageAvailabilityRequest) =>
  http.post<BulkPackageAvailabilityResponse>(`/api/packages/${packageId}/availability/bulk`, body).then((r) => r.data)

/** Cambiar cupo o abrir/cerrar una salida puntual. */
export const updatePackageAvailability = (http: AxiosInstance, packageId: string, availabilityId: string, body: UpdateAvailabilityRequest) =>
  http.patch<PackageAvailabilityResponse>(`/api/packages/${packageId}/availability/${availabilityId}`, body).then((r) => r.data)
