import {
  EMPTY_FILTERS,
  countActiveFilters,
  toExperienceParams,
  toPackageParams,
  today,
  type CatalogFilters,
} from '@/features/catalog/filters'

/**
 * Este módulo es el contrato entre lo que la persona toca y lo que acepta el backend. Si acá se cuela
 * un parámetro inventado, la búsqueda lo ignora en silencio — por eso se prueba la traducción entera.
 */

describe('today', () => {
  it('devuelve la fecha local en formato DateOnly, no el instante UTC', () => {
    // 21:30 del 12 en una zona con -04: en UTC ya sería el 13, pero el backend espera el día local.
    const date = new Date('2026-10-13T01:30:00.000Z')
    const spy = jest.spyOn(date, 'getTimezoneOffset').mockReturnValue(240)

    expect(today(date)).toBe('2026-10-12')

    spy.mockRestore()
  })
})

describe('toExperienceParams', () => {
  it('no manda ningún filtro cuando no hay ninguno puesto', () => {
    const params = toExperienceParams(EMPTY_FILTERS)

    expect(params.destinationId).toBeUndefined()
    expect(params.categoryId).toBeUndefined()
    expect(params.priceMax).toBeUndefined()
    expect(params.availableFrom).toBeUndefined()
  })

  it('traduce disponibilidad a una fecha, que es lo que el backend entiende', () => {
    const params = toExperienceParams({ ...EMPTY_FILTERS, onlyWithAvailability: true })

    expect(params.availableFrom).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })

  it('pasa destino, categoría y precio tal cual', () => {
    const params = toExperienceParams({
      destinationId: 'd1',
      categoryId: 'c1',
      priceMax: 300,
      onlyWithAvailability: false,
    })

    expect(params).toMatchObject({ destinationId: 'd1', categoryId: 'c1', priceMax: 300 })
  })

  it('ignora la duración, que solo existe para paquetes', () => {
    const params = toExperienceParams({ ...EMPTY_FILTERS, durationDays: { min: 1, max: 3 } })

    expect(Object.keys(params)).not.toContain('durationDaysMin')
    expect(Object.keys(params)).not.toContain('durationDaysMax')
  })

  it('siempre pide un pageSize, para que el scroll infinito tenga páginas', () => {
    expect(toExperienceParams(EMPTY_FILTERS).pageSize).toBeGreaterThan(0)
  })
})

describe('toPackageParams', () => {
  it('usa departureFrom y no availableFrom', () => {
    const params = toPackageParams({ ...EMPTY_FILTERS, onlyWithAvailability: true })

    expect(params.departureFrom).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    expect(Object.keys(params)).not.toContain('availableFrom')
  })

  it('desarma la duración en sus dos parámetros', () => {
    const params = toPackageParams({ ...EMPTY_FILTERS, durationDays: { min: 4, max: 7 } })

    expect(params).toMatchObject({ durationDaysMin: 4, durationDaysMax: 7 })
  })

  it('deja el máximo abierto en el tramo "8 días o más"', () => {
    const params = toPackageParams({ ...EMPTY_FILTERS, durationDays: { min: 8, max: undefined } })

    expect(params.durationDaysMin).toBe(8)
    expect(params.durationDaysMax).toBeUndefined()
  })
})

describe('countActiveFilters', () => {
  const full: CatalogFilters = {
    destinationId: 'd1',
    categoryId: 'c1',
    priceMax: 300,
    onlyWithAvailability: true,
    durationDays: { min: 1, max: 3 },
  }

  it('no cuenta nada sin filtros', () => {
    expect(countActiveFilters(EMPTY_FILTERS, 'experiences')).toBe(0)
  })

  it('cuenta la duración solo en paquetes', () => {
    expect(countActiveFilters(full, 'packages')).toBe(5)
    expect(countActiveFilters(full, 'experiences')).toBe(4)
  })

  it('no cuenta la disponibilidad cuando está apagada', () => {
    expect(countActiveFilters({ ...EMPTY_FILTERS, onlyWithAvailability: false }, 'experiences')).toBe(0)
  })

  it('cuenta un precio de 0 como filtro puesto', () => {
    expect(countActiveFilters({ ...EMPTY_FILTERS, priceMax: 0 }, 'experiences')).toBe(1)
  })
})
