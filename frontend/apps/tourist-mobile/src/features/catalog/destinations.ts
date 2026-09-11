import type { PublicDestinationResponse } from '@turisclick/api-client'

/**
 * El endpoint devuelve las ciudades en orden alfabético, así que "Bermejo" y "Cabezas" quedan primero y
 * La Paz aparece recién en la posición 12 del carrusel. Se reordena por `publishedExperienceCount`, que
 * es un número que **calcula el backend** — no una métrica inventada de popularidad ni de valoración.
 *
 * No se oculta ninguna ciudad: ese contador solo mira experiencias, y una ciudad podría tener paquetes
 * sin tener experiencias. Ordenar sí, esconder no.
 */
export function byPublishedContent(destinations?: PublicDestinationResponse[] | null) {
  return [...(destinations ?? [])].sort((a, b) => {
    const diff = (b.publishedExperienceCount ?? 0) - (a.publishedExperienceCount ?? 0)
    return diff !== 0 ? diff : (a.name ?? '').localeCompare(b.name ?? '')
  })
}

/** Etiqueta del contador. Devuelve null cuando no hay nada que contar, para no mostrar "0 experiencias". */
export function experienceCountLabel(count?: number) {
  if (!count) return null
  return `${count} ${count === 1 ? 'experiencia' : 'experiencias'}`
}
