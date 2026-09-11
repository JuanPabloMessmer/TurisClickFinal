import { byPublishedContent, experienceCountLabel } from '@/features/catalog/destinations'

/**
 * El orden viene de `publishedExperienceCount`, un número que calcula el backend. No es una métrica de
 * popularidad ni de valoración inventada por la app.
 */

describe('byPublishedContent', () => {
  it('pone primero los destinos con más experiencias publicadas', () => {
    const sorted = byPublishedContent([
      { id: '1', name: 'Bermejo', publishedExperienceCount: 0 },
      { id: '2', name: 'La Paz', publishedExperienceCount: 12 },
      { id: '3', name: 'Uyuni', publishedExperienceCount: 3 },
    ])

    expect(sorted.map((d) => d.name)).toEqual(['La Paz', 'Uyuni', 'Bermejo'])
  })

  it('desempata alfabéticamente', () => {
    const sorted = byPublishedContent([
      { id: '1', name: 'Sucre', publishedExperienceCount: 2 },
      { id: '2', name: 'Oruro', publishedExperienceCount: 2 },
    ])

    expect(sorted.map((d) => d.name)).toEqual(['Oruro', 'Sucre'])
  })

  it('no esconde los destinos sin experiencias: podrían tener paquetes', () => {
    const sorted = byPublishedContent([
      { id: '1', name: 'Bermejo', publishedExperienceCount: 0 },
      { id: '2', name: 'La Paz', publishedExperienceCount: 5 },
    ])

    expect(sorted).toHaveLength(2)
  })

  it('trata el contador ausente como cero en vez de romperse', () => {
    const sorted = byPublishedContent([{ id: '1', name: 'Sin dato' }, { id: '2', name: 'Con dato', publishedExperienceCount: 1 }])

    expect(sorted[0].name).toBe('Con dato')
  })

  it('no muta el array recibido', () => {
    const cities = [
      { id: '1', name: 'Bermejo', publishedExperienceCount: 0 },
      { id: '2', name: 'La Paz', publishedExperienceCount: 12 },
    ]

    byPublishedContent(cities)

    expect(cities[0].name).toBe('Bermejo')
  })

  it('tolera que no haya datos todavía', () => {
    expect(byPublishedContent(undefined)).toEqual([])
    expect(byPublishedContent(null)).toEqual([])
  })
})

describe('experienceCountLabel', () => {
  it('no dice "0 experiencias"', () => {
    expect(experienceCountLabel(0)).toBeNull()
    expect(experienceCountLabel(undefined)).toBeNull()
  })

  it('singulariza el 1', () => {
    expect(experienceCountLabel(1)).toBe('1 experiencia')
    expect(experienceCountLabel(7)).toBe('7 experiencias')
  })
})
