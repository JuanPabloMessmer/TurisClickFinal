/**
 * Lógica pura de calendario compartida por Tourist Mobile (elegir fecha) y Backoffice (programar
 * disponibilidad). Trabaja con fechas "YYYY-MM-DD" en horario LOCAL: una disponibilidad es un día de
 * calendario, no un instante, así que nunca se pasa por `new Date("YYYY-MM-DD")` (que la leería en UTC).
 */

export type IsoDate = string

/** Día de la semana como en JavaScript y en el backend: 0 = domingo … 6 = sábado. */
export type Weekday = 0 | 1 | 2 | 3 | 4 | 5 | 6

export interface CalendarCell {
  iso: IsoDate
  day: number
  /** false para los días de relleno del mes anterior/siguiente. */
  inMonth: boolean
  weekday: Weekday
}

/** Encabezados empezando en lunes, como en los calendarios de Bolivia. */
export const WEEKDAY_SHORT_LABELS = ['Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb', 'Dom'] as const

/** Orden lunes → domingo expresado en índices JS, para pintar selectores de días. */
export const WEEKDAYS_MONDAY_FIRST: Weekday[] = [1, 2, 3, 4, 5, 6, 0]

export const WEEKDAY_LONG_LABELS: Record<Weekday, string> = {
  0: 'Domingo',
  1: 'Lunes',
  2: 'Martes',
  3: 'Miércoles',
  4: 'Jueves',
  5: 'Viernes',
  6: 'Sábado',
}

export const WEEKDAY_INITIALS: Record<Weekday, string> = { 0: 'D', 1: 'L', 2: 'M', 3: 'X', 4: 'J', 5: 'V', 6: 'S' }

/** Días que selecciona cada preset (espejo de AvailabilitySchedule en el backend). */
export const PRESET_WEEKDAYS: Record<'EVERY_DAY' | 'WEEKDAYS' | 'WEEKENDS', Weekday[]> = {
  EVERY_DAY: [0, 1, 2, 3, 4, 5, 6],
  WEEKDAYS: [1, 2, 3, 4, 5],
  WEEKENDS: [0, 6],
}

const pad = (n: number) => String(n).padStart(2, '0')

export function toIsoDate(date: Date): IsoDate {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function parseIsoDate(iso: IsoDate): Date {
  const [year, month, day] = iso.slice(0, 10).split('-').map(Number)
  return new Date(year, month - 1, day)
}

export function todayIso(now: Date = new Date()): IsoDate {
  return toIsoDate(now)
}

export function addDaysIso(iso: IsoDate, days: number): IsoDate {
  const date = parseIsoDate(iso)
  date.setDate(date.getDate() + days)
  return toIsoDate(date)
}

export interface YearMonth {
  year: number
  /** 0 = enero. */
  month: number
}

export function yearMonthOf(iso: IsoDate): YearMonth {
  const date = parseIsoDate(iso)
  return { year: date.getFullYear(), month: date.getMonth() }
}

export function addMonths({ year, month }: YearMonth, delta: number): YearMonth {
  const total = year * 12 + month + delta
  return { year: Math.floor(total / 12), month: ((total % 12) + 12) % 12 }
}

export function compareYearMonth(a: YearMonth, b: YearMonth): number {
  return a.year * 12 + a.month - (b.year * 12 + b.month)
}

/** "Septiembre 2026". */
export function monthLabel({ year, month }: YearMonth): string {
  const name = new Intl.DateTimeFormat('es-BO', { month: 'long' }).format(new Date(year, month, 1))
  return `${name.charAt(0).toUpperCase()}${name.slice(1)} ${year}`
}

/**
 * Semanas del mes (lunes a domingo), con relleno del mes anterior/siguiente para que todas las filas
 * tengan 7 celdas. Siempre 4 a 6 filas.
 */
export function buildMonthGrid({ year, month }: YearMonth): CalendarCell[][] {
  const first = new Date(year, month, 1)
  // Cuántos días del mes anterior hacen falta para arrancar en lunes.
  const leading = (first.getDay() + 6) % 7
  const start = new Date(year, month, 1 - leading)
  const daysInMonth = new Date(year, month + 1, 0).getDate()
  const totalCells = Math.ceil((leading + daysInMonth) / 7) * 7

  const weeks: CalendarCell[][] = []
  for (let i = 0; i < totalCells; i++) {
    const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i)
    if (i % 7 === 0) weeks.push([])
    weeks[weeks.length - 1].push({
      iso: toIsoDate(date),
      day: date.getDate(),
      inMonth: date.getMonth() === month,
      weekday: date.getDay() as Weekday,
    })
  }
  return weeks
}

/** Agrupa por día de calendario conservando el orden de entrada dentro de cada día. */
export function groupByDate<T>(items: T[], dateOf: (item: T) => string | null | undefined): Map<IsoDate, T[]> {
  const map = new Map<IsoDate, T[]>()
  for (const item of items) {
    const raw = dateOf(item)
    if (!raw) continue
    const key = raw.slice(0, 10)
    const list = map.get(key)
    if (list) list.push(item)
    else map.set(key, [item])
  }
  return map
}

/** Fechas del rango (extremos incluidos) que caen en los días elegidos — vista previa del alta masiva. */
export function expandWeekdayPattern(startIso: IsoDate, endIso: IsoDate, weekdays: Weekday[]): IsoDate[] {
  if (!startIso || !endIso || endIso < startIso || weekdays.length === 0) return []
  const wanted = new Set(weekdays)
  const result: IsoDate[] = []
  const cursor = parseIsoDate(startIso)
  const end = parseIsoDate(endIso)
  // Techo defensivo: la UI nunca debería pedir más de ~2 años.
  for (let guard = 0; cursor <= end && guard < 800; guard++) {
    if (wanted.has(cursor.getDay() as Weekday)) result.push(toIsoDate(cursor))
    cursor.setDate(cursor.getDate() + 1)
  }
  return result
}

/** Qué preset coincide con una selección de días, o CUSTOM si ninguno. */
export function presetForWeekdays(weekdays: Weekday[]): 'EVERY_DAY' | 'WEEKDAYS' | 'WEEKENDS' | 'CUSTOM' {
  const key = [...new Set(weekdays)].sort().join(',')
  for (const preset of ['EVERY_DAY', 'WEEKDAYS', 'WEEKENDS'] as const) {
    if ([...PRESET_WEEKDAYS[preset]].sort().join(',') === key) return preset
  }
  return 'CUSTOM'
}
