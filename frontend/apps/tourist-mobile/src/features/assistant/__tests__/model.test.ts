import { httpError, networkError } from '@/test-utils'
import {
  availabilityBadge,
  canBook,
  canSave,
  describeBookingFailure,
  groupByDay,
  lastAssistantIndex,
  quickRepliesFor,
} from '@/features/assistant/model'

describe('groupByDay', () => {
  it('agrupa por día y ordena días e ítems', () => {
    const days = groupByDay([
      { id: 'c', dayNumber: 2, sortOrder: 0 },
      { id: 'b', dayNumber: 1, sortOrder: 1 },
      { id: 'a', dayNumber: 1, sortOrder: 0 },
    ])
    expect(days.map((d) => d.dayNumber)).toEqual([1, 2])
    expect(days[0].items.map((i) => i.id)).toEqual(['a', 'b'])
    expect(groupByDay(null)).toEqual([])
  })
})

describe('availabilityBadge', () => {
  it('traduce el estado real del backend', () => {
    expect(availabilityBadge({ availabilityState: 'AVAILABLE', currentAvailableSlots: 3 })).toEqual({ label: 'Disponible · 3 lugares', tone: 'success' })
    expect(availabilityBadge({ availabilityState: 'SOLD_OUT' }).tone).toBe('danger')
    expect(availabilityBadge({ availabilityState: 'UNPUBLISHED' }).label).toBe('Ya no disponible')
  })
})

describe('quickRepliesFor', () => {
  it('ofrece respuestas para cada dato que falta, con destinos reales', () => {
    const replies = quickRepliesFor(['destino', 'cantidad de viajeros'], ['La Paz', 'Sucre'])
    expect(replies.map((r) => r.message)).toEqual(['Quiero ir a La Paz', 'Quiero ir a Sucre', 'Viajo solo', 'Somos 2 personas', 'Somos 4 personas'])
    expect(quickRepliesFor([], ['La Paz'])).toEqual([])
  })
})

describe('acciones sobre el itinerario', () => {
  const base = { id: 'it', items: [{ id: 'x' }], isStillBookable: true }

  it('guardar solo en borrador; reservar si sigue siendo reservable', () => {
    expect(canSave({ ...base, status: 'DRAFT' })).toBe(true)
    expect(canSave({ ...base, status: 'SAVED' })).toBe(false)
    expect(canBook({ ...base, status: 'SAVED' })).toBe(true)
    expect(canBook({ ...base, status: 'BOOKED' })).toBe(false)
    expect(canBook({ ...base, status: 'DRAFT', isStillBookable: false })).toBe(false)
    expect(canBook({ ...base, status: 'DRAFT', items: [] })).toBe(false)
  })

  it('explica los errores reales del booking', () => {
    expect(describeBookingFailure(httpError(409, { errorCode: 'INSUFFICIENT_CAPACITY', detail: 'x' })).message).toMatch(/no se reservó nada/)
    expect(describeBookingFailure(httpError(409, { errorCode: 'ITINERARY_ALREADY_BOOKED', detail: 'x' })).checkTrips).toBe(true)
    expect(describeBookingFailure(networkError())).toMatchObject({ checkTrips: true })
  })
})

it('lastAssistantIndex encuentra la última respuesta del asistente', () => {
  expect(lastAssistantIndex([{ sender: 'TOURIST' }, { sender: 'AI' }, { sender: 'TOURIST' }])).toBe(1)
  expect(lastAssistantIndex([{ sender: 'TOURIST' }])).toBe(-1)
})
