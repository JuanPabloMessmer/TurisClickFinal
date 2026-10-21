import type { providerFlightsApi } from '@turisclick/api-client'

type Departure = providerFlightsApi.ProviderFlightDepartureResponse
type DepartureRequest = providerFlightsApi.ProviderFlightDepartureRequest

export interface DepartureFormValues {
  departureAt: string
  arrivalAt: string
  totalSeats: string
  adultPrice: string
  childPrice: string
  infantPrice: string
  currency: string
}

const MAX_DAYS_AHEAD = 330
const MAX_DURATION_HOURS = 48

/** "yyyy-MM-ddTHH:mm" del input → "yyyy-MM-ddTHH:mm:ss" SIN huso: es la hora local del aeropuerto. */
export function toLocalIso(value: string): string {
  return value.length === 16 ? `${value}:00` : value
}

/** Las mismas reglas que ProviderFlightDepartureRequest en el backend, con sus mismos mensajes. */
export function validateDeparture(v: DepartureFormValues, today = new Date()): Partial<Record<keyof DepartureFormValues, string>> {
  const errors: Partial<Record<keyof DepartureFormValues, string>> = {}
  if (!v.departureAt) errors.departureAt = 'Indicá la fecha y hora de salida.'
  if (!v.arrivalAt) errors.arrivalAt = 'Indicá la fecha y hora de llegada.'
  if (v.departureAt && v.arrivalAt) {
    const dep = new Date(v.departureAt)
    const arr = new Date(v.arrivalAt)
    const todayStart = new Date(today.getFullYear(), today.getMonth(), today.getDate())
    const limit = new Date(todayStart)
    limit.setDate(limit.getDate() + MAX_DAYS_AHEAD)
    if (arr <= dep) errors.arrivalAt = 'La llegada tiene que ser posterior a la salida.'
    else if (arr.getTime() - dep.getTime() > MAX_DURATION_HOURS * 3_600_000)
      errors.arrivalAt = `Un vuelo no puede durar más de ${MAX_DURATION_HOURS} horas.`
    if (dep < todayStart) errors.departureAt = 'La salida ya pasó.'
    else if (dep > limit) errors.departureAt = `Las salidas se publican hasta ${MAX_DAYS_AHEAD} días antes.`
  }
  const seats = Number(v.totalSeats)
  if (!Number.isInteger(seats) || seats < 1 || seats > 500) errors.totalSeats = 'Los asientos van de 1 a 500.'
  for (const key of ['adultPrice', 'childPrice', 'infantPrice'] as const) {
    const n = Number(v[key])
    if (v[key] === '' || Number.isNaN(n) || n < 0 || n > 1_000_000) errors[key] = 'Ingresá un precio entre 0 y 1.000.000.'
  }
  if (!/^[A-Z]{3}$/.test(v.currency)) errors.currency = 'La moneda es un código ISO de tres letras, por ejemplo BOB o USD.'
  return errors
}

export function toDepartureRequest(v: DepartureFormValues): DepartureRequest {
  return {
    departureAt: toLocalIso(v.departureAt),
    arrivalAt: toLocalIso(v.arrivalAt),
    totalSeats: Number(v.totalSeats),
    adultPrice: Number(v.adultPrice),
    childPrice: Number(v.childPrice),
    infantPrice: Number(v.infantPrice),
    currency: v.currency,
  }
}

export const EMPTY_DEPARTURE: DepartureFormValues = {
  departureAt: '',
  arrivalAt: '',
  totalSeats: '',
  adultPrice: '',
  childPrice: '',
  infantPrice: '0',
  currency: 'BOB',
}

export function fromDeparture(d: Departure): DepartureFormValues {
  return {
    departureAt: (d.departureAt ?? '').slice(0, 16),
    arrivalAt: (d.arrivalAt ?? '').slice(0, 16),
    totalSeats: String(d.totalSeats ?? ''),
    adultPrice: String(d.adultPrice ?? ''),
    childPrice: String(d.childPrice ?? ''),
    infantPrice: String(d.infantPrice ?? '0'),
    currency: d.currency ?? 'BOB',
  }
}
