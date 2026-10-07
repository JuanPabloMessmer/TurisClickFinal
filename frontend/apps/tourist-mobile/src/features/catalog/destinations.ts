import type { PublicDestinationResponse } from '@turisclick/api-client'

/**
 * El endpoint devuelve las ciudades en orden alfabético, así que "Bermejo" y "Cabezas" quedan primero y
 * La Paz aparece recién en la posición 12 del carrusel. Se reordena por `publishedExperienceCount`, que
 * es un número que **calcula el backend** — no una métrica inventada de popularidad ni de valoración.
 *
 * Ordena por todo lo publicado —experiencias y paquetes—, no sólo por experiencias: una ciudad puede vender
 * únicamente paquetes de varios días, y con el contador de experiencias quedaba al final como si estuviera
 * vacía.
 */
export function byPublishedContent(destinations?: PublicDestinationResponse[] | null) {
  return [...(destinations ?? [])].sort((a, b) => {
    const diff = publishedCount(b) - publishedCount(a)
    return diff !== 0 ? diff : (a.name ?? '').localeCompare(b.name ?? '')
  })
}

/** Todo lo que se puede reservar en esa ciudad hoy. Los dos contadores los calcula el backend. */
export function publishedCount(destination: PublicDestinationResponse) {
  return (destination.publishedExperienceCount ?? 0) + (destination.publishedPackageCount ?? 0)
}

/**
 * Si la ciudad tiene algo que reservar.
 *
 * El carrusel de la portada dice "ciudades con experiencias reales para reservar", y abrir una que está vacía
 * contradice la promesa: el viajero toca una foto linda y cae en una lista en blanco. Las ciudades sin nada
 * publicado no desaparecen del producto —siguen estando en el filtro de Explorar, que es donde tiene sentido
 * buscar— pero no se ofrecen como destino a descubrir.
 */
export function hasPublishedContent(destination: PublicDestinationResponse) {
  return publishedCount(destination) > 0
}

/** Etiqueta del contador. Devuelve null cuando no hay nada que contar, para no mostrar "0 experiencias". */
export function experienceCountLabel(count?: number) {
  if (!count) return null
  return `${count} ${count === 1 ? 'experiencia' : 'experiencias'}`
}

/** Igual, para paquetes. */
export function packageCountLabel(count?: number) {
  if (!count) return null
  return `${count} ${count === 1 ? 'paquete' : 'paquetes'}`
}

/**
 * Lo que se puede reservar en esa ciudad, nombrado. Cuenta las dos cosas porque el orden del carrusel también
 * las cuenta: una ciudad que vende sólo paquetes aparecía arriba diciendo "1 experiencia", y el número no
 * explicaba por qué estaba ahí.
 */
export function contentCountLabel(destination: PublicDestinationResponse) {
  const parts = [
    experienceCountLabel(destination.publishedExperienceCount),
    packageCountLabel(destination.publishedPackageCount),
  ].filter(Boolean)

  return parts.length > 0 ? parts.join(' · ') : null
}
