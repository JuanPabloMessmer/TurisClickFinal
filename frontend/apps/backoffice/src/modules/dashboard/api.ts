import { useQueries, useQuery } from '@tanstack/react-query'
import { categoriesApi, companiesApi, destinationsApi, experiencesApi } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

/**
 * Datos de "Hoy". Regla dura: todo sale de endpoints que YA existen, y nada se estima. Si un número no
 * se puede obtener sin inventar, no se muestra — un dashboard que miente es peor que no tenerlo.
 *
 * `pageSize: 1` es el truco barato para los contadores: `PagedResult.totalCount` viene igual y no se
 * traen 100 filas para mostrar un número.
 */

export function usePendingCompanies() {
  return useQuery({
    queryKey: ['dashboard', 'pendingCompanies'],
    queryFn: () => companiesApi.listCompanies(httpClient, { status: 'PENDING', page: 1, pageSize: 5 }),
  })
}

export function useApprovedCompaniesCount() {
  return useQuery({
    queryKey: ['dashboard', 'approvedCompanies'],
    queryFn: () => companiesApi.listCompanies(httpClient, { status: 'APPROVED', page: 1, pageSize: 1 }),
  })
}

export function useDestinationsCount() {
  return useQuery({
    queryKey: ['dashboard', 'destinations'],
    queryFn: () => destinationsApi.listDestinations(httpClient),
  })
}

export function useCategoriesCount() {
  return useQuery({
    queryKey: ['dashboard', 'categories'],
    queryFn: () => categoriesApi.listCategories(httpClient),
  })
}

/** Cuántas experiencias publicadas se consultan por disponibilidad: acotado para no disparar N requests sin techo. */
export const AVAILABILITY_LOOKUP_LIMIT = 20

export interface UpcomingDeparture {
  experienceId: string
  experienceTitle: string
  availabilityId: string
  date: string
  startTime?: string | null
  totalSlots: number
  reservedSlots: number
}

/**
 * Próximas salidas del operador. El listado de experiencias no trae disponibilidad (el summary del
 * backend no la incluye), así que se consulta por producto y se arma acá. Es una consulta por
 * experiencia publicada, con tope y cacheada: el día que el backend exponga la próxima fecha en el
 * summary, esto se reemplaza por un solo request (ver docs/ui-debt.md).
 */
export function useUpcomingDepartures(
  experiences: { id?: string; title?: string | null; status?: string | null }[],
) {
  const published = experiences
    .filter((experience) => experience.status === 'PUBLISHED' && experience.id)
    .slice(0, AVAILABILITY_LOOKUP_LIMIT)

  const results = useQueries({
    queries: published.map((experience) => ({
      queryKey: ['ownedAvailability', experience.id],
      queryFn: () => experiencesApi.listOwnedAvailability(httpClient, experience.id!),
      staleTime: 60_000,
    })),
  })

  const today = new Date().toISOString().slice(0, 10)
  const departures: UpcomingDeparture[] = []
  const withoutFutureDates: string[] = []

  results.forEach((result, index) => {
    const experience = published[index]
    if (!result.data) return

    const future = result.data
      .filter((slot) => (slot.date ?? '') >= today && slot.status === 'OPEN')
      .sort((a, b) => (a.date ?? '').localeCompare(b.date ?? ''))

    if (future.length === 0) {
      withoutFutureDates.push(experience.title ?? 'Sin título')
      return
    }

    future.slice(0, 2).forEach((slot) => {
      departures.push({
        experienceId: experience.id!,
        experienceTitle: experience.title ?? 'Sin título',
        availabilityId: slot.id!,
        date: slot.date!,
        startTime: slot.startTime,
        totalSlots: slot.totalSlots ?? 0,
        reservedSlots: slot.reservedSlots ?? 0,
      })
    })
  })

  return {
    isLoading: results.some((result) => result.isPending),
    departures: departures.sort((a, b) => a.date.localeCompare(b.date)).slice(0, 6),
    withoutFutureDates,
    /** Cuántas experiencias publicadas quedaron fuera del tope, para no afirmar más de lo que se miró. */
    notInspected: Math.max(
      experiences.filter((experience) => experience.status === 'PUBLISHED').length - AVAILABILITY_LOOKUP_LIMIT,
      0,
    ),
  }
}
