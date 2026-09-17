import {
  addMonths,
  buildMonthGrid,
  compareYearMonth,
  groupByDate,
  yearMonthOf,
  type CalendarCell,
  type IsoDate,
  type YearMonth,
} from '@turisclick/utils'
import type { BookableSlot } from '@/features/booking/selection'

/** Estado visual de un día. `past` y `unavailable` no se pueden tocar. */
export type DayState = 'past' | 'unavailable' | 'available' | 'selected' | 'outside'

export interface CalendarDay extends CalendarCell {
  state: DayState
  slots: BookableSlot[]
}

/** Slots con cupo agrupados por día, cada día ordenado por horario (día completo primero). */
export function slotsByDate(slots: BookableSlot[]): Map<IsoDate, BookableSlot[]> {
  const bookable = slots.filter((slot) => slot.availableSlots > 0)
  const grouped = groupByDate(bookable, (slot) => slot.date)
  for (const list of grouped.values()) {
    list.sort((a, b) => (a.time ?? '').localeCompare(b.time ?? ''))
  }
  return grouped
}

export function buildCalendarMonth(
  month: YearMonth,
  byDate: Map<IsoDate, BookableSlot[]>,
  today: IsoDate,
  selectedDate: IsoDate | null,
): CalendarDay[][] {
  return buildMonthGrid(month).map((week) =>
    week.map((cell) => {
      const slots = cell.inMonth ? byDate.get(cell.iso) ?? [] : []
      let state: DayState
      if (!cell.inMonth) state = 'outside'
      else if (cell.iso < today) state = 'past'
      else if (slots.length === 0) state = 'unavailable'
      else if (cell.iso === selectedDate) state = 'selected'
      else state = 'available'
      return { ...cell, state, slots }
    }),
  )
}

/** Primer día con cupo a partir de hoy, o null. */
export function firstAvailableDate(byDate: Map<IsoDate, BookableSlot[]>, today: IsoDate): IsoDate | null {
  const dates = [...byDate.keys()].filter((date) => date >= today).sort()
  return dates[0] ?? null
}

export function lastAvailableDate(byDate: Map<IsoDate, BookableSlot[]>): IsoDate | null {
  const dates = [...byDate.keys()].sort()
  return dates.at(-1) ?? null
}

/**
 * Límites de navegación: no se retrocede antes del mes actual ni se avanza más allá del último mes con
 * disponibilidad (mirar meses vacíos no ayuda a reservar).
 */
export function navigationBounds(byDate: Map<IsoDate, BookableSlot[]>, today: IsoDate) {
  const min = yearMonthOf(today)
  const last = lastAvailableDate(byDate)
  const max = last && last > today ? yearMonthOf(last) : min
  return { min, max }
}

export function canGo(month: YearMonth, delta: number, bounds: { min: YearMonth; max: YearMonth }) {
  const target = addMonths(month, delta)
  return compareYearMonth(target, bounds.min) >= 0 && compareYearMonth(target, bounds.max) <= 0
}

/** Cantidad de días con cupo en un mes — para el resumen bajo el encabezado. */
export function availableDaysInMonth(month: YearMonth, byDate: Map<IsoDate, BookableSlot[]>, today: IsoDate) {
  return [...byDate.keys()].filter((date) => date >= today && compareYearMonth(yearMonthOf(date), month) === 0).length
}
