import type { AvailabilityPresetValue } from '@turisclick/api-client'
import { expandWeekdayPattern, PRESET_WEEKDAYS, presetForWeekdays, type Weekday } from '@turisclick/utils'

/**
 * Estado y reglas del formulario "Programar disponibilidad". Lógica pura (sin React) para poder testearla
 * y compartirla entre experiencias (con horarios) y paquetes (una salida por fecha).
 */
export interface ScheduleFormState {
  startDate: string
  endDate: string
  preset: AvailabilityPresetValue
  weekdays: Weekday[]
  /** "HH:mm". Vacío = día completo. Los paquetes no usan horarios. */
  startTimes: string[]
  totalSlots: number
}

/** Espejo de AvailabilitySchedule en el backend: el formulario avisa antes de que el servidor rechace. */
export const MAX_RANGE_DAYS = 366
export const MAX_SLOTS_PER_REQUEST = 1000

export function initialScheduleForm(today: string): ScheduleFormState {
  return { startDate: today, endDate: '', preset: 'WEEKDAYS', weekdays: [...PRESET_WEEKDAYS.WEEKDAYS], startTimes: ['09:00'], totalSlots: 10 }
}

/** Elegir un preset reemplaza la selección de días; "Personalizado" conserva la actual. */
export function applyPreset(state: ScheduleFormState, preset: AvailabilityPresetValue): ScheduleFormState {
  if (preset === 'CUSTOM') return { ...state, preset }
  return { ...state, preset, weekdays: [...PRESET_WEEKDAYS[preset]] }
}

/** Tocar un día pasa a la selección manual, y si coincide con un preset lo refleja. */
export function toggleWeekday(state: ScheduleFormState, day: Weekday): ScheduleFormState {
  const weekdays = state.weekdays.includes(day) ? state.weekdays.filter((d) => d !== day) : [...state.weekdays, day]
  return { ...state, weekdays, preset: presetForWeekdays(weekdays) }
}

export function uniqueTimes(times: string[]): string[] {
  return [...new Set(times.map((t) => t.trim()).filter(Boolean))].sort()
}

export interface ScheduleEstimate {
  dates: string[]
  timesPerDate: number
  total: number
}

export function estimateSchedule(state: ScheduleFormState, withTimes: boolean): ScheduleEstimate {
  const dates = expandWeekdayPattern(state.startDate, state.endDate, state.weekdays)
  const timesPerDate = withTimes ? Math.max(1, uniqueTimes(state.startTimes).length) : 1
  return { dates, timesPerDate, total: dates.length * timesPerDate }
}

/** Primer problema del formulario, o null si se puede enviar. */
export function validateSchedule(state: ScheduleFormState, withTimes: boolean, today: string): string | null {
  if (!state.startDate || !state.endDate) return 'Elegí la fecha inicial y la final.'
  if (state.startDate < today) return 'La fecha inicial no puede ser pasada.'
  if (state.endDate < state.startDate) return 'La fecha final no puede ser anterior a la inicial.'
  const days = Math.round((Date.parse(state.endDate) - Date.parse(state.startDate)) / 86_400_000) + 1
  if (days > MAX_RANGE_DAYS) return `El rango no puede superar ${MAX_RANGE_DAYS} días.`
  if (state.weekdays.length === 0) return 'Elegí al menos un día de la semana.'
  if (!Number.isInteger(state.totalSlots) || state.totalSlots < 1 || state.totalSlots > 10_000) return 'La capacidad debe ser un número entre 1 y 10.000.'
  const estimate = estimateSchedule(state, withTimes)
  if (estimate.total === 0) return 'El patrón elegido no produce ninguna fecha dentro del rango.'
  if (estimate.total > MAX_SLOTS_PER_REQUEST) return `Se generarían ${estimate.total} disponibilidades; el máximo por operación es ${MAX_SLOTS_PER_REQUEST}.`
  return null
}

/** Cuerpo del POST .../availability/bulk. Los horarios viajan como "HH:mm:ss" (TimeOnly). */
export function buildBulkRequest(state: ScheduleFormState, withTimes: boolean, dryRun: boolean) {
  const base = {
    startDate: state.startDate,
    endDate: state.endDate,
    preset: state.preset,
    weekdays: state.preset === 'CUSTOM' ? [...state.weekdays].sort() : [],
    totalSlots: state.totalSlots,
    dryRun,
  }
  return withTimes ? { ...base, startTimes: uniqueTimes(state.startTimes).map((t) => (t.length === 5 ? `${t}:00` : t)) } : base
}
