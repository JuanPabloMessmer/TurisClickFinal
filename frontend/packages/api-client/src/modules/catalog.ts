import type { AxiosInstance } from 'axios'
import type {
  CategoryResponse,
  ExperienceAvailabilityResponse,
  ExperienceResponse,
  ExperienceSummaryResponsePagedResult,
  PackageAvailabilityResponse,
  PackageResponse,
  PackageSummaryResponsePagedResult,
} from '../types'

/**
 * Cara PÚBLICA del catálogo (UC-T-03/04/05/06/07): todo `AllowAnonymous`, pensado para que Tourist
 * Mobile pueda explorar sin sesión. Vive aparte de `experiences.ts`/`packages.ts`, que exponen la cara
 * de gestión del PROVIDER (crear, publicar, "mis productos") y exigen su rol.
 */

export interface ExperienceSearchParams {
  destinationId?: string
  categoryId?: string
  priceMin?: number
  priceMax?: number
  /** Solo experiencias con al menos un slot disponible desde esta fecha (YYYY-MM-DD). */
  availableFrom?: string
  page?: number
  pageSize?: number
}

export interface PackageSearchParams {
  destinationId?: string
  categoryId?: string
  priceMin?: number
  priceMax?: number
  durationDaysMin?: number
  durationDaysMax?: number
  /** Solo paquetes con alguna salida desde esta fecha (YYYY-MM-DD). */
  departureFrom?: string
  page?: number
  pageSize?: number
}

/** Catálogo maestro de categorías, solo lectura — el filtro `categoryId` de las búsquedas sale de acá. */
export const listCategories = (http: AxiosInstance) =>
  http.get<CategoryResponse[]>('/api/categories').then((r) => r.data)

/** UC-T-04 — resultados ordenados por fecha de creación descendente (los más nuevos primero). */
export const searchExperiences = (http: AxiosInstance, params?: ExperienceSearchParams) =>
  http.get<ExperienceSummaryResponsePagedResult>('/api/experiences', { params }).then((r) => r.data)

/** UC-T-05 — 404 si no está publicada o su empresa está suspendida. */
export const getExperience = (http: AxiosInstance, id: string) =>
  http.get<ExperienceResponse>(`/api/experiences/${id}`).then((r) => r.data)

/** Slots futuros con cupo de una experiencia publicada. */
export const getExperienceAvailability = (http: AxiosInstance, experienceId: string) =>
  http.get<ExperienceAvailabilityResponse[]>(`/api/experiences/${experienceId}/availability`).then((r) => r.data)

/** UC-T-06 — mismo orden que las experiencias. */
export const searchPackages = (http: AxiosInstance, params?: PackageSearchParams) =>
  http.get<PackageSummaryResponsePagedResult>('/api/packages', { params }).then((r) => r.data)

/** UC-T-07. */
export const getPackage = (http: AxiosInstance, id: string) =>
  http.get<PackageResponse>(`/api/packages/${id}`).then((r) => r.data)

/** Salidas futuras con cupo de un paquete publicado. */
export const getPackageAvailability = (http: AxiosInstance, packageId: string) =>
  http.get<PackageAvailabilityResponse[]>(`/api/packages/${packageId}/availability`).then((r) => r.data)
