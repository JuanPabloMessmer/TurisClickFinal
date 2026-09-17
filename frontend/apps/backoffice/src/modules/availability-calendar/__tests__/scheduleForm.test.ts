import { describe, expect, it } from 'vitest'
import {
  applyPreset,
  buildBulkRequest,
  estimateSchedule,
  initialScheduleForm,
  MAX_SLOTS_PER_REQUEST,
  toggleWeekday,
  validateSchedule,
  type ScheduleFormState,
} from '../scheduleForm'

const TODAY = '2026-10-01' // jueves

const form = (overrides: Partial<ScheduleFormState> = {}): ScheduleFormState => ({
  ...initialScheduleForm(TODAY),
  endDate: '2026-10-31',
  ...overrides,
})

describe('presets y días', () => {
  it('los presets seleccionan sus días y Personalizado conserva la selección', () => {
    expect(applyPreset(form(), 'WEEKENDS').weekdays.sort()).toEqual([0, 6])
    expect(applyPreset(form(), 'EVERY_DAY').weekdays).toHaveLength(7)
    const custom = applyPreset(form({ weekdays: [2, 4] }), 'CUSTOM')
    expect(custom).toMatchObject({ preset: 'CUSTOM', weekdays: [2, 4] })
  })

  it('tocar un día pasa a personalizado, y vuelve a un preset si coincide', () => {
    const withoutFriday = toggleWeekday(form(), 5)
    expect(withoutFriday.preset).toBe('CUSTOM')
    expect(toggleWeekday(withoutFriday, 5).preset).toBe('WEEKDAYS')
  })
})

describe('estimación', () => {
  it('multiplica fechas por horarios distintos (experiencias)', () => {
    const estimate = estimateSchedule(form({ startTimes: ['09:00', '15:00', '09:00', ''] }), true)
    expect(estimate.dates).toHaveLength(22) // días hábiles de octubre 2026
    expect(estimate.timesPerDate).toBe(2)
    expect(estimate.total).toBe(44)
  })

  it('los paquetes ignoran horarios', () => {
    expect(estimateSchedule(form({ startTimes: ['09:00', '15:00'] }), false).total).toBe(22)
  })
})

describe('validación', () => {
  it('acepta un formulario completo', () => {
    expect(validateSchedule(form(), true, TODAY)).toBeNull()
  })

  it.each([
    [{ endDate: '' }, 'Elegí la fecha inicial y la final.'],
    [{ startDate: '2026-09-30' }, 'La fecha inicial no puede ser pasada.'],
    [{ endDate: '2026-09-30', startDate: '2026-10-05' }, 'La fecha final no puede ser anterior a la inicial.'],
    [{ endDate: '2027-12-31' }, 'El rango no puede superar 366 días.'],
    [{ weekdays: [], preset: 'CUSTOM' as const }, 'Elegí al menos un día de la semana.'],
    [{ totalSlots: 0 }, 'La capacidad debe ser un número entre 1 y 10.000.'],
    [{ startDate: '2026-10-05', endDate: '2026-10-09', weekdays: [0, 6] as (0 | 6)[] }, 'El patrón elegido no produce ninguna fecha dentro del rango.'],
  ])('rechaza %o', (overrides, message) => {
    expect(validateSchedule(form(overrides), true, TODAY)).toBe(message)
  })

  it('avisa antes de superar el máximo por operación', () => {
    const times = Array.from({ length: 12 }, (_, h) => `${String(h + 8).padStart(2, '0')}:00`)
    const result = validateSchedule(form({ endDate: '2027-03-31', preset: 'EVERY_DAY', weekdays: [0, 1, 2, 3, 4, 5, 6], startTimes: times }), true, TODAY)
    expect(result).toContain(`el máximo por operación es ${MAX_SLOTS_PER_REQUEST}`)
  })
})

describe('request', () => {
  it('experiencias: horarios en HH:mm:ss sin repetidos; los presets no mandan días', () => {
    expect(buildBulkRequest(form({ startTimes: ['15:00', '09:00', '15:00'] }), true, true)).toEqual({
      startDate: TODAY,
      endDate: '2026-10-31',
      preset: 'WEEKDAYS',
      weekdays: [],
      totalSlots: 10,
      dryRun: true,
      startTimes: ['09:00:00', '15:00:00'],
    })
  })

  it('personalizado manda los días; paquetes no mandan horarios', () => {
    const body = buildBulkRequest(form({ preset: 'CUSTOM', weekdays: [4, 2] }), false, false)
    expect(body).toMatchObject({ preset: 'CUSTOM', weekdays: [2, 4], dryRun: false })
    expect(body).not.toHaveProperty('startTimes')
  })
})
