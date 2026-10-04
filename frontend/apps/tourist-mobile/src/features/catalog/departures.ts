import type { ExperienceAvailabilityResponse } from '@turisclick/api-client'
import { todayIso } from '@turisclick/utils'

/**
 * Próximas salidas: el gesto propio de TurisClick (DESIGN.md §1). Lo que decide una compra de turismo
 * no es el precio solo, es "¿sale cuando yo puedo ir y queda lugar?".
 *
 * Todo lo que sale de acá viene de disponibilidad REAL del backend. No se inventa urgencia: si un
 * producto no tiene fechas futuras abiertas, la tira no se muestra — no se escribe "¡últimos lugares!"
 * ni nada que el dato no respalde.
 */

export interface Departure {
  id: string
  date: string
  startTime?: string | null
  availableSlots: number
}

export const DEPARTURES_PREVIEW = 2

export function nextDepartures(
  slots: ExperienceAvailabilityResponse[] | undefined,
  limit = DEPARTURES_PREVIEW,
): Departure[] {
  const today = todayIso()

  return (slots ?? [])
    .filter((slot) => slot.status === 'OPEN' && (slot.availableSlots ?? 0) > 0 && (slot.date ?? '') >= today)
    .sort((a, b) => (a.date ?? '').localeCompare(b.date ?? '') || (a.startTime ?? '').localeCompare(b.startTime ?? ''))
    .slice(0, limit)
    .map((slot) => ({
      id: slot.id!,
      date: slot.date!,
      startTime: slot.startTime,
      availableSlots: slot.availableSlots ?? 0,
    }))
}

/** "sáb 11 oct" — corto, en minúscula, sin el año: el año solo estorba cuando la fecha es cercana. */
export function shortDateLabel(iso: string): string {
  const [year, month, day] = iso.split('-').map(Number)
  const formatted = new Intl.DateTimeFormat('es-BO', { weekday: 'short', day: 'numeric', month: 'short' }).format(
    new Date(year, month - 1, day),
  )
  return formatted.replace(/\./g, '')
}

export function slotsLabel(availableSlots: number): string {
  return `${availableSlots} ${availableSlots === 1 ? 'lugar' : 'lugares'}`
}
