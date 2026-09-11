import type { ExperienceFilters, PackageFilters } from '@/features/catalog/queries'

/**
 * Estado de los filtros de Explorar y su traducción a query params.
 *
 * Regla: acá solo existen filtros que el backend soporta de verdad
 * (`GET /api/experiences` y `GET /api/packages`). Nada de ordenar por popularidad, valoración ni
 * relevancia: esos parámetros no existen.
 */

export type CatalogTab = 'experiences' | 'packages'

export interface CatalogFilters {
  destinationId?: string
  categoryId?: string
  /** Tope de precio. El backend compara contra el monto publicado, sin convertir monedas. */
  priceMax?: number
  /** Solo productos con cupo a partir de hoy. El backend lo recibe como fecha, no como booleano. */
  onlyWithAvailability: boolean
  /** Solo aplica a paquetes; el backend de experiencias no tiene equivalente. */
  durationDays?: { min?: number; max?: number }
}

export const EMPTY_FILTERS: CatalogFilters = { onlyWithAvailability: false }

export const PRICE_MAX_OPTIONS = [100, 300, 600, 1000] as const

export const DURATION_OPTIONS = [
  { label: '1 a 3 días', min: 1, max: 3 },
  { label: '4 a 7 días', min: 4, max: 7 },
  { label: '8 días o más', min: 8, max: undefined },
] as const

const PAGE_SIZE = 10

/** YYYY-MM-DD en hora local — el backend espera un DateOnly, no un instante UTC. */
export function today(now = new Date()): string {
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 10)
}

export function toExperienceParams(filters: CatalogFilters): ExperienceFilters {
  return {
    destinationId: filters.destinationId,
    categoryId: filters.categoryId,
    priceMax: filters.priceMax,
    availableFrom: filters.onlyWithAvailability ? today() : undefined,
    pageSize: PAGE_SIZE,
  }
}

export function toPackageParams(filters: CatalogFilters): PackageFilters {
  return {
    destinationId: filters.destinationId,
    categoryId: filters.categoryId,
    priceMax: filters.priceMax,
    departureFrom: filters.onlyWithAvailability ? today() : undefined,
    durationDaysMin: filters.durationDays?.min,
    durationDaysMax: filters.durationDays?.max,
    pageSize: PAGE_SIZE,
  }
}

/**
 * Cuántos filtros hay puestos, para el contador del botón. La duración cuenta como uno solo aunque
 * viaje como dos parámetros, porque para quien usa la app es una sola decisión.
 */
export function countActiveFilters(filters: CatalogFilters, tab: CatalogTab): number {
  let count = 0
  if (filters.destinationId) count++
  if (filters.categoryId) count++
  if (filters.priceMax != null) count++
  if (filters.onlyWithAvailability) count++
  if (tab === 'packages' && filters.durationDays) count++
  return count
}
