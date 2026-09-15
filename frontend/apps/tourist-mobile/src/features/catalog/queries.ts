import { catalogApi, destinationsApi } from '@turisclick/api-client'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { httpClient } from '@/lib/httpClient'

/**
 * Hooks del catálogo público. Todos golpean endpoints AllowAnonymous, así que funcionan con o sin
 * sesión — es lo que permite explorar la app antes de crear una cuenta.
 *
 * Las pantallas no hablan con axios: screen → hook → api-client → backend.
 */

export type ExperienceFilters = catalogApi.ExperienceSearchParams
export type PackageFilters = catalogApi.PackageSearchParams

export const catalogKeys = {
  all: ['catalog'] as const,
  categories: ['catalog', 'categories'] as const,
  cities: ['catalog', 'destinations', 'cities'] as const,
  experiences: (params: ExperienceFilters) => ['catalog', 'experiences', params] as const,
  experiencesInfinite: (params: ExperienceFilters) => ['catalog', 'experiences', 'infinite', params] as const,
  experience: (id: string) => ['catalog', 'experience', id] as const,
  experienceAvailability: (id: string) => ['catalog', 'experience', id, 'availability'] as const,
  packages: (params: PackageFilters) => ['catalog', 'packages', params] as const,
  packagesInfinite: (params: PackageFilters) => ['catalog', 'packages', 'infinite', params] as const,
  package: (id: string) => ['catalog', 'package', id] as const,
  packageAvailability: (id: string) => ['catalog', 'package', id, 'availability'] as const,
}

/** Los catálogos maestros casi no cambian: no tiene sentido revalidarlos seguido. */
const MASTER_DATA_STALE_TIME = 30 * 60_000

export function useCategories() {
  return useQuery({
    queryKey: catalogKeys.categories,
    queryFn: () => catalogApi.listCategories(httpClient),
    staleTime: MASTER_DATA_STALE_TIME,
  })
}

/** Solo ciudades: es el nivel al que cuelgan Experiences y Packages (regla del dominio). */
export function useCities() {
  return useQuery({
    queryKey: catalogKeys.cities,
    queryFn: () => destinationsApi.listPublicDestinations(httpClient, { type: 'CITY' }),
    staleTime: MASTER_DATA_STALE_TIME,
  })
}

/**
 * `PagedResult` expone `page` y `totalPages`, así que la página siguiente sale de ahí. Se devuelve
 * `undefined` en la última, que es como TanStack Query sabe que no hay más para pedir.
 */
export function nextPageFrom(lastPage: { page?: number; totalPages?: number }) {
  const page = lastPage.page ?? 1
  return page < (lastPage.totalPages ?? 0) ? page + 1 : undefined
}

export function useExperiences(params: ExperienceFilters) {
  return useQuery({
    queryKey: catalogKeys.experiences(params),
    queryFn: () => catalogApi.searchExperiences(httpClient, params),
  })
}

export function useInfiniteExperiences(params: ExperienceFilters) {
  return useInfiniteQuery({
    queryKey: catalogKeys.experiencesInfinite(params),
    queryFn: ({ pageParam }) => catalogApi.searchExperiences(httpClient, { ...params, page: pageParam }),
    initialPageParam: 1,
    getNextPageParam: nextPageFrom,
  })
}

export function useExperience(id: string) {
  return useQuery({
    queryKey: catalogKeys.experience(id),
    queryFn: () => catalogApi.getExperience(httpClient, id),
    enabled: Boolean(id),
  })
}

export function useExperienceAvailability(id: string) {
  return useQuery({
    queryKey: catalogKeys.experienceAvailability(id),
    queryFn: () => catalogApi.getExperienceAvailability(httpClient, id),
    enabled: Boolean(id),
  })
}

export function usePackages(params: PackageFilters) {
  return useQuery({
    queryKey: catalogKeys.packages(params),
    queryFn: () => catalogApi.searchPackages(httpClient, params),
  })
}

export function useInfinitePackages(params: PackageFilters) {
  return useInfiniteQuery({
    queryKey: catalogKeys.packagesInfinite(params),
    queryFn: ({ pageParam }) => catalogApi.searchPackages(httpClient, { ...params, page: pageParam }),
    initialPageParam: 1,
    getNextPageParam: nextPageFrom,
  })
}

export function usePackage(id: string) {
  return useQuery({
    queryKey: catalogKeys.package(id),
    queryFn: () => catalogApi.getPackage(httpClient, id),
    enabled: Boolean(id),
  })
}

export function usePackageAvailability(id: string) {
  return useQuery({
    queryKey: catalogKeys.packageAvailability(id),
    queryFn: () => catalogApi.getPackageAvailability(httpClient, id),
    enabled: Boolean(id),
  })
}
