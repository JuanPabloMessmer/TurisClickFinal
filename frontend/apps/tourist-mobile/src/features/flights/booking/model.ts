import { providerFlightsApi, type flightsApi, type ReservationItemResponse } from '@turisclick/api-client'
import {
  CABINS,
  CHILD_MAX_AGE,
  CHILD_MIN_AGE,
  MAX_PASSENGERS,
  type SearchParams,
} from '@/features/flights/search/model'

/**
 * Reglas de la reserva de un vuelo de operador, sin React. Los límites son los mismos que valida el backend
 * (`CreateReservationRequest` y `ReservationPassengerRequest`): acá se repiten para no dejar a la persona
 * corregir un dato después de tocar "Reservar", no porque la app sea la autoridad.
 *
 * Contrato: docs/vuelos-sueltos-contratos.md §3.
 */

type Option = flightsApi.FlightOptionResponse
type PassengerRequest = providerFlightsApi.ReservationPassengerRequest
export type PassengerType = 'ADULT' | 'CHILD' | 'INFANT'

const { ADULT, CHILD, INFANT } = providerFlightsApi.PASSENGER_TYPE

// ---------------------------------------------------------------- qué se puede reservar

/**
 * Una opción se ofrece para reservar sólo si es un vuelo de operador **y** el backend dice que se puede, con
 * una o dos salidas. Una oferta de aerolínea (la del proveedor simulado) nunca: aunque algún día llegara con
 * `bookable: true`, la app no tiene cómo comprarla suelta y ofrecerla sería prometer algo que no pasa.
 */
export function isBookableProviderOption(option: Option): boolean {
  const ids = option.providerFlight?.departureIds ?? []
  return option.source === providerFlightsApi.OFFER_SOURCE.PROVIDER && option.bookable === true && ids.length >= 1 && ids.length <= 2
}

/** Con qué salidas se reserva la opción, en el orden ida → vuelta que manda el backend. */
export function departureIdsOf(option: Option): string[] {
  return option.providerFlight?.departureIds ?? []
}

/** La opción elegida dentro de una búsqueda, por sus salidas. Si ya no está (se agotó, se cerró), null. */
export function findSelectedOption(options: Option[] | null | undefined, departureIds: string[]): Option | null {
  const key = departureIds.join(',')
  return (options ?? []).find((option) => isBookableProviderOption(option) && departureIdsOf(option).join(',') === key) ?? null
}

// ---------------------------------------------------------------- la ruta de la reserva

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** Lo que viaja en la URL de la reserva: la búsqueda (para volver a encontrar la opción) y las salidas. */
export type FlightBookingParams = SearchParams & { ids: string }

/** De un pedido de búsqueda a los parámetros de la URL: el inverso de `requestFromParams`. */
export function searchParamsFromRequest(request: flightsApi.SearchFlightsRequest): SearchParams {
  const children = request.childrenAges ?? []
  return {
    o: request.originIata ?? '',
    d: request.destinationIata ?? '',
    dep: request.departureDate ?? '',
    ...(request.returnDate ? { ret: request.returnDate } : {}),
    ad: String(request.adults ?? 1),
    ...(children.length > 0 ? { ch: children.join(',') } : {}),
    ...((request.infants ?? 0) > 0 ? { inf: String(request.infants) } : {}),
    cab: CABINS.some((c) => c.value === request.cabinClass) ? (request.cabinClass as string) : 'ECONOMY',
  }
}

export function bookingParams(request: flightsApi.SearchFlightsRequest, option: Option): FlightBookingParams {
  return { ...searchParamsFromRequest(request), ids: departureIdsOf(option).join(',') }
}

/**
 * Las salidas de la URL. Null si no tienen forma de id o si no cuadran con el tipo de viaje: sólo ida es una
 * salida, ida y vuelta son dos distintas. Una URL editada a mano no llega al backend como cualquier cosa.
 */
export function departureIdsFromParam(value: string | string[] | undefined, roundTrip: boolean): string[] | null {
  const raw = Array.isArray(value) ? value[0] : value
  const ids = (raw ?? '').split(',').filter(Boolean)
  if (ids.length !== (roundTrip ? 2 : 1)) return null
  if (!ids.every((id) => UUID.test(id))) return null
  if (new Set(ids).size !== ids.length) return null
  return ids
}

// ---------------------------------------------------------------- pasajeros

/** Un lugar del formulario: el tipo lo fija la búsqueda (la tarifa se cotizó con esa mezcla de pasajeros). */
export interface PassengerSlot {
  type: PassengerType
  /** Sólo niños: la edad que se declaró al buscar. */
  searchedAge?: number
}

export interface PassengerDraft {
  givenName: string
  familyName: string
  bornOn: string
}

export type PassengerErrors = Partial<Record<keyof PassengerDraft, string>>

export const emptyPassenger = (): PassengerDraft => ({ givenName: '', familyName: '', bornOn: '' })

/** Adultos primero, después niños (con su edad) y al final infantes: el orden en que se emiten los pasajes. */
export function passengerSlots(request: Pick<flightsApi.SearchFlightsRequest, 'adults' | 'childrenAges' | 'infants'>): PassengerSlot[] {
  return [
    ...Array.from({ length: request.adults ?? 1 }, (): PassengerSlot => ({ type: ADULT })),
    ...(request.childrenAges ?? []).map((age): PassengerSlot => ({ type: CHILD, searchedAge: age })),
    ...Array.from({ length: request.infants ?? 0 }, (): PassengerSlot => ({ type: INFANT })),
  ]
}

export function passengerTypeLabel(slot: PassengerSlot): string {
  switch (slot.type) {
    case ADULT:
      return 'Adulto · 12 años o más'
    case CHILD:
      return slot.searchedAge != null ? `Niño · ${slot.searchedAge} años` : 'Niño · de 2 a 11 años'
    default:
      return 'Infante · menor de 2 años, en brazos'
  }
}

const NAME = /^\p{L}[\p{L} '\-.]*$/u
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/
const MAX_NAME = 60

/** Una fecha YYYY-MM-DD que existe en el calendario (sin 2024-02-31). */
export function isRealDate(value: string): boolean {
  const match = ISO_DATE.exec(value)
  if (!match) return false
  const [, y, m, d] = match.map(Number)
  const date = new Date(Date.UTC(y, m - 1, d))
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d
}

/** Años cumplidos en `on` (ambas YYYY-MM-DD). Comparar texto evita husos: son fechas, no instantes. */
export function ageOn(bornOn: string, on: string): number {
  const years = Number(on.slice(0, 4)) - Number(bornOn.slice(0, 4))
  return on.slice(5) < bornOn.slice(5) ? years - 1 : years
}

export interface TravelDates {
  /** Día de la ida, YYYY-MM-DD (hora local del aeropuerto). */
  departure: string
  /** Día de la vuelta, si es ida y vuelta. Hoy las edades se validan contra la ida, como en el backend. */
  return?: string | null
  /** Hoy, YYYY-MM-DD. Se pasa para que los tests sean determinísticos. */
  today: string
}

/**
 * La edad tiene que corresponder al tipo **el día de la ida**, no el día en que se reserva — la misma regla
 * que aplica el backend (B2): adulto 12 o más, niño de 2 a 11, infante menor de 2.
 */
export function validatePassenger(draft: PassengerDraft, slot: PassengerSlot, dates: TravelDates): PassengerErrors {
  const errors: PassengerErrors = {}
  const given = draft.givenName.trim()
  const family = draft.familyName.trim()

  if (!given) errors.givenName = 'Escribí un nombre.'
  else if (given.length > MAX_NAME || !NAME.test(given)) errors.givenName = 'Sólo letras, espacios, apóstrofos y guiones.'

  if (!family) errors.familyName = 'Escribí un apellido.'
  else if (family.length > MAX_NAME || !NAME.test(family)) errors.familyName = 'Sólo letras, espacios, apóstrofos y guiones.'

  const born = draft.bornOn.trim()
  if (!isRealDate(born)) {
    errors.bornOn = 'Usá el formato año-mes-día, por ejemplo 1990-05-14.'
    return errors
  }
  if (born >= dates.today) {
    errors.bornOn = 'La fecha de nacimiento tiene que ser pasada.'
    return errors
  }
  if (ageOn(born, dates.today) > 120) {
    errors.bornOn = 'Revisá la fecha de nacimiento.'
    return errors
  }

  const age = ageOn(born, dates.departure)
  if (slot.type === ADULT && age < CHILD_MAX_AGE + 1) {
    errors.bornOn = `Un adulto tiene que tener ${CHILD_MAX_AGE + 1} años o más el día del vuelo.`
  } else if (slot.type === CHILD && (age < CHILD_MIN_AGE || age > CHILD_MAX_AGE)) {
    errors.bornOn = `Un niño tiene que tener de ${CHILD_MIN_AGE} a ${CHILD_MAX_AGE} años el día del vuelo.`
  } else if (slot.type === INFANT && age >= CHILD_MIN_AGE) {
    errors.bornOn = 'Un infante tiene que ser menor de 2 años el día del vuelo.'
  }

  return errors
}

export const hasErrors = (errors: PassengerErrors) => Object.keys(errors).length > 0

/**
 * Las reglas de la mezcla, las mismas que el backend: al menos un adulto, no más infantes que adultos (cada
 * uno viaja en brazos) y hasta 9 pasajeros contando todos. Null si está bien.
 */
export function passengerMixProblem(slots: PassengerSlot[]): string | null {
  const adults = slots.filter((s) => s.type === ADULT).length
  const infants = slots.filter((s) => s.type === INFANT).length
  if (slots.length === 0) return 'Faltan los pasajeros.'
  if (slots.length > MAX_PASSENGERS) return `Se pueden reservar hasta ${MAX_PASSENGERS} pasajeros, contando niños e infantes.`
  if (adults === 0) return 'Tiene que viajar al menos un adulto.'
  if (infants > adults) return 'Cada infante viaja en brazos de un adulto: no puede haber más infantes que adultos.'
  return null
}

/** Con asiento: adultos y niños. Es lo que el backend espera en `travelers`. */
export function seatedCount(slots: PassengerSlot[]): number {
  return slots.filter((s) => s.type !== INFANT).length
}

/** El pedido de B1: salidas, pasajeros sintéticos (sin documento) y `travelers` = pasajeros con asiento. */
export function buildFlightReservationRequest(departureIds: string[], slots: PassengerSlot[], drafts: PassengerDraft[]) {
  const passengers: PassengerRequest[] = slots.map((slot, index) => ({
    type: slot.type,
    givenName: drafts[index].givenName.trim(),
    familyName: drafts[index].familyName.trim(),
    bornOn: drafts[index].bornOn.trim(),
  }))
  return { flightDepartureIds: departureIds, passengers, travelers: seatedCount(slots) }
}

// ---------------------------------------------------------------- datos de ejemplo

const SAMPLE_GIVEN = ['Ana', 'Luis', 'Carla', 'Diego', 'Elena', 'Mateo', 'Sofía', 'Tomás', 'Valeria']

/** YYYY-MM-DD de `years` años y `days` días antes de `from`. */
function yearsBefore(from: string, years: number, days = 0): string {
  const date = new Date(`${from}T00:00:00Z`)
  date.setUTCFullYear(date.getUTCFullYear() - years)
  date.setUTCDate(date.getUTCDate() - days)
  return date.toISOString().slice(0, 10)
}

/**
 * Pasajeros ficticios que cumplen las reglas de edad para esas fechas. Es una demostración: no hace falta (ni
 * se quiere) que nadie escriba sus datos reales. El apellido "Prueba" lo deja claro en cualquier listado.
 */
export function samplePassengers(slots: PassengerSlot[], dates: TravelDates): PassengerDraft[] {
  return slots.map((slot, index) => {
    const bornOn =
      slot.type === ADULT
        ? yearsBefore(dates.departure, 30 + index)
        : slot.type === CHILD
          ? // La edad declarada al buscar, cumplida hace un mes: sigue valiendo el día del vuelo.
            yearsBefore(dates.departure, slot.searchedAge ?? 7, 30)
          : // Seis meses al salir: sigue siendo menor de 2 también a la vuelta.
            yearsBefore(dates.departure, 0, 180)
    return { givenName: SAMPLE_GIVEN[index % SAMPLE_GIVEN.length], familyName: 'Prueba', bornOn }
  })
}

// ---------------------------------------------------------------- después de reservar: la emisión

type FlightItem = NonNullable<ReservationItemResponse['flight']>

export type IssuanceTone = 'warning' | 'success' | 'danger' | 'neutral'

export interface IssuanceDisplay {
  kind: 'AWAITING_PAYMENT' | 'PENDING' | 'ISSUED' | 'FAILED' | 'UNKNOWN'
  label: string
  message: string
  tone: IssuanceTone
}

/**
 * En qué está el pasaje, en las palabras del contrato. La regla que no se negocia: **"Pasaje emitido" sólo con
 * `issuanceStatus = ISSUED`**, que únicamente escribe el operador al registrar la emisión. Pagar no emite.
 */
export function issuanceDisplay(flight: FlightItem, itemStatus?: string | null): IssuanceDisplay {
  const operator = flight.operatorName?.trim() || 'el operador'
  const status = flight.issuanceStatus

  if (status === providerFlightsApi.ISSUANCE_STATUS.ISSUED) {
    return {
      kind: 'ISSUED',
      label: 'Pasaje emitido',
      message: `Pasaje emitido por ${operator}.`,
      tone: 'success',
    }
  }
  if (status === providerFlightsApi.ISSUANCE_STATUS.FAILED) {
    return {
      kind: 'FAILED',
      label: 'Emisión fallida · reembolso total',
      message: `${capitalize(operator)} no pudo emitir el pasaje. Se canceló este vuelo y se te devuelve el total.`,
      tone: 'danger',
    }
  }
  if (itemStatus === 'PENDING_PAYMENT') {
    return {
      kind: 'AWAITING_PAYMENT',
      label: 'Pendiente de pago',
      message: `Cuando pagues, ${operator} emite el pasaje y lo registra acá.`,
      tone: 'warning',
    }
  }
  if (status === providerFlightsApi.ISSUANCE_STATUS.PENDING && itemStatus === 'CONFIRMED') {
    return {
      kind: 'PENDING',
      label: 'Emisión pendiente',
      message: `Pago confirmado · emisión pendiente por ${operator}. Te avisamos acá cuando registre el pasaje.`,
      tone: 'warning',
    }
  }
  return { kind: 'UNKNOWN', label: 'Sin pasaje', message: 'Este vuelo no tiene un pasaje emitido.', tone: 'neutral' }
}

/** Todo vuelo de operador lleva DEMO salvo que el backend diga explícitamente lo contrario. */
export function isDemoFlight(flight: FlightItem): boolean {
  return flight.demo !== false
}

/** Ítems de vuelo de operador de una reserva, en orden de salida (ida y después vuelta). */
export function flightItemsOf(items: ReservationItemResponse[] | null | undefined): ReservationItemResponse[] {
  return (items ?? [])
    .filter((item) => item.flight)
    .sort((a, b) => (a.flight!.departureAt ?? '').localeCompare(b.flight!.departureAt ?? ''))
}

export function passengersSummaryOf(flight: FlightItem): string {
  const passengers = flight.passengers ?? []
  const count = (type: string) => passengers.filter((p) => p.type === type).length
  const parts: string[] = []
  const adults = count(ADULT)
  const children = count(CHILD)
  const infants = count(INFANT)
  if (adults) parts.push(`${adults} ${adults === 1 ? 'adulto' : 'adultos'}`)
  if (children) parts.push(`${children} ${children === 1 ? 'niño' : 'niños'}`)
  if (infants) parts.push(`${infants} ${infants === 1 ? 'infante' : 'infantes'}`)
  return parts.length > 0 ? parts.join(', ') : 'Sin pasajeros informados'
}

function capitalize(text: string) {
  return text.charAt(0).toUpperCase() + text.slice(1)
}
