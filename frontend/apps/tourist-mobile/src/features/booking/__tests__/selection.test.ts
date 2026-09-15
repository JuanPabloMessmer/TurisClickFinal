import {
  MAX_TRAVELERS_PER_RESERVATION,
  clampTravelers,
  estimatedTotal,
  maxTravelersFor,
} from '@/features/booking/selection'

const slot = (availableSlots: number) => ({ id: 's', availableSlots })

describe('viajeros', () => {
  it('el máximo es min(availableSlots, 100)', () => {
    expect(maxTravelersFor(slot(3))).toBe(3)
    expect(maxTravelersFor(slot(250))).toBe(MAX_TRAVELERS_PER_RESERVATION)
  })

  it('nunca baja de 1', () => {
    expect(clampTravelers(0, slot(5))).toBe(1)
    expect(clampTravelers(-4, slot(5))).toBe(1)
  })

  it('se ajusta al cupo de la fecha elegida', () => {
    expect(clampTravelers(8, slot(3))).toBe(3)
    expect(clampTravelers(150, slot(500))).toBe(100)
  })
})

describe('estimatedTotal', () => {
  it('es precio × viajeros, redondeado a centavos', () => {
    expect(estimatedTotal(40, 3)).toBe(120)
    expect(estimatedTotal(19.99, 3)).toBe(59.97)
  })

  it('sin precio no inventa un total', () => {
    expect(estimatedTotal(undefined, 2)).toBeNull()
  })
})
