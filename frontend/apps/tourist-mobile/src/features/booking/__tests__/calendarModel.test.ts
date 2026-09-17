import { buildMonthGrid, expandWeekdayPattern, presetForWeekdays } from '@turisclick/utils'
import { buildCalendarMonth, canGo, firstAvailableDate, navigationBounds, slotsByDate } from '@/features/booking/calendarModel'

describe('buildMonthGrid', () => {
  it('arranca en lunes y rellena con días de otros meses', () => {
    const weeks = buildMonthGrid({ year: 2026, month: 8 }) // septiembre 2026: 1 cae martes

    expect(weeks[0][0]).toMatchObject({ iso: '2026-08-31', inMonth: false, weekday: 1 })
    expect(weeks[0][1]).toMatchObject({ iso: '2026-09-01', day: 1, inMonth: true })
    expect(weeks.every((week) => week.length === 7)).toBe(true)
    expect(weeks.flat().filter((cell) => cell.inMonth)).toHaveLength(30)
  })

  it('febrero de 2027 que arranca en lunes ocupa 4 semanas exactas', () => {
    expect(buildMonthGrid({ year: 2027, month: 1 })).toHaveLength(4)
  })
})

describe('modelo del calendario de reserva', () => {
  const slots = [
    { id: 'late', date: '2026-09-20', time: '15:00:00', availableSlots: 1 },
    { id: 'early', date: '2026-09-20', time: '09:00:00', availableSlots: 4 },
    { id: 'full', date: '2026-09-21', time: '09:00:00', availableSlots: 0 },
    { id: 'old', date: '2026-09-10', time: null, availableSlots: 5 },
    { id: 'next', date: '2026-11-02', time: null, availableSlots: 5 },
  ]
  const byDate = slotsByDate(slots)

  it('agrupa solo lo que tiene cupo y ordena por horario', () => {
    expect(byDate.get('2026-09-20')?.map((s) => s.id)).toEqual(['early', 'late'])
    expect(byDate.has('2026-09-21')).toBe(false)
  })

  it('asigna estados por día', () => {
    const days = buildCalendarMonth({ year: 2026, month: 8 }, byDate, '2026-09-16', '2026-09-20').flat()
    const state = (iso: string) => days.find((d) => d.iso === iso)?.state

    expect(state('2026-09-10')).toBe('past')
    expect(state('2026-09-20')).toBe('selected')
    expect(state('2026-09-21')).toBe('unavailable')
    expect(state('2026-08-31')).toBe('outside')
  })

  it('primera fecha futura y límites de navegación', () => {
    expect(firstAvailableDate(byDate, '2026-09-16')).toBe('2026-09-20')
    const bounds = navigationBounds(byDate, '2026-09-16')
    expect(bounds).toEqual({ min: { year: 2026, month: 8 }, max: { year: 2026, month: 10 } })
    expect(canGo({ year: 2026, month: 8 }, -1, bounds)).toBe(false)
    expect(canGo({ year: 2026, month: 9 }, 1, bounds)).toBe(true)
    expect(canGo({ year: 2026, month: 10 }, 1, bounds)).toBe(false)
  })
})

describe('patrones del calendario del proveedor', () => {
  it('expande un rango según los días elegidos', () => {
    // 2026-10-01 es jueves.
    expect(expandWeekdayPattern('2026-10-01', '2026-10-07', [0, 6])).toEqual(['2026-10-03', '2026-10-04'])
    expect(expandWeekdayPattern('2026-10-07', '2026-10-01', [1])).toEqual([])
    expect(expandWeekdayPattern('2026-10-01', '2026-10-31', [1, 2, 3, 4, 5])).toHaveLength(22)
  })

  it('reconoce el preset que corresponde a una selección', () => {
    expect(presetForWeekdays([1, 2, 3, 4, 5])).toBe('WEEKDAYS')
    expect(presetForWeekdays([6, 0])).toBe('WEEKENDS')
    expect(presetForWeekdays([0, 1, 2, 3, 4, 5, 6])).toBe('EVERY_DAY')
    expect(presetForWeekdays([2, 4])).toBe('CUSTOM')
  })
})
