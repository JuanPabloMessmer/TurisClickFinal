import type { flightsApi } from '@turisclick/api-client'
import { addDaysIso, type IsoDate } from '@turisclick/utils'

/**
 * Reglas del buscador de vuelos, sin React. Viven acá para poder probarlas solas y para que la pantalla no
 * invente reglas propias: los límites son los mismos que valida el backend (`SearchFlightsRequest`) y que
 * imponen los proveedores aéreos. Si cambian allá, cambian acá.
 */

// ---------------------------------------------------------------- límites (los del backend)

export const MAX_PASSENGERS = 9
export const MAX_DAYS_AHEAD = 330
export const CHILD_MIN_AGE = 2
export const CHILD_MAX_AGE = 11
export const CHILD_AGES = Array.from({ length: CHILD_MAX_AGE - CHILD_MIN_AGE + 1 }, (_, i) => CHILD_MIN_AGE + i)

// ---------------------------------------------------------------- pasajeros y cabina

export type TripType = 'ONE_WAY' | 'ROUND_TRIP'
export type Cabin = 'ECONOMY' | 'PREMIUM_ECONOMY' | 'BUSINESS' | 'FIRST'

export const CABINS: { value: Cabin; label: string }[] = [
  { value: 'ECONOMY', label: 'Económica' },
  { value: 'PREMIUM_ECONOMY', label: 'Premium economy' },
  { value: 'BUSINESS', label: 'Business' },
  { value: 'FIRST', label: 'Primera' },
]

export function cabinLabel(cabin?: string | null) {
  return CABINS.find((c) => c.value === cabin)?.label ?? 'Económica'
}

/** Niños con asiento (2 a 11 años, cada uno con su edad) e infantes en brazos (menores de 2). */
export interface PassengerMix {
  adults: number
  childrenAges: number[]
  infants: number
}

export const DEFAULT_PASSENGERS: PassengerMix = { adults: 1, childrenAges: [], infants: 0 }

export function totalPassengers(mix: PassengerMix) {
  return mix.adults + mix.childrenAges.length + mix.infants
}

/** Lo que se puede tocar en cada contador. La UI deshabilita el botón; nunca corrige en silencio. */
export function passengerLimits(mix: PassengerMix) {
  const full = totalPassengers(mix) >= MAX_PASSENGERS
  return {
    canAddAdult: !full,
    // Un adulto menos no puede dejar a un infante sin brazos.
    canRemoveAdult: mix.adults > 1 && mix.adults - 1 >= mix.infants,
    canAddChild: !full,
    canRemoveChild: mix.childrenAges.length > 0,
    canAddInfant: !full && mix.infants < mix.adults,
    canRemoveInfant: mix.infants > 0,
    full,
  }
}

/** Por qué no se puede sumar un infante, si no se puede. Es lo que se le explica a la persona. */
export function infantBlockReason(mix: PassengerMix): string | null {
  if (totalPassengers(mix) >= MAX_PASSENGERS) return `Se pueden buscar hasta ${MAX_PASSENGERS} pasajeros.`
  if (mix.infants >= mix.adults) return 'Cada infante viaja en brazos de un adulto.'
  return null
}

export function passengersSummary(mix: PassengerMix) {
  const parts = [`${mix.adults} ${mix.adults === 1 ? 'adulto' : 'adultos'}`]
  if (mix.childrenAges.length > 0) parts.push(`${mix.childrenAges.length} ${mix.childrenAges.length === 1 ? 'niño' : 'niños'}`)
  if (mix.infants > 0) parts.push(`${mix.infants} ${mix.infants === 1 ? 'infante' : 'infantes'}`)
  return parts.join(', ')
}

// ---------------------------------------------------------------- el borrador de búsqueda

export interface AirportChoice {
  iata: string
  city: string
  name: string
  country: string
  countryCode: string
}

export function airportFromResult(result: flightsApi.AirportSearchResultResponse): AirportChoice {
  return {
    iata: result.iata ?? '',
    city: result.city ?? result.iata ?? '',
    name: result.name ?? '',
    country: result.country ?? '',
    countryCode: result.countryCode ?? '',
  }
}

export interface SearchDraft {
  origin: AirportChoice | null
  destination: AirportChoice | null
  tripType: TripType
  departureDate: IsoDate | null
  returnDate: IsoDate | null
  passengers: PassengerMix
  cabin: Cabin
}

/** Ida y vuelta a dos y tres semanas: buscar no debería exigir tocar el calendario si las fechas dan igual. */
export function initialDraft(today: IsoDate): SearchDraft {
  return {
    origin: null,
    destination: null,
    tripType: 'ROUND_TRIP',
    departureDate: addDaysIso(today, 14),
    returnDate: addDaysIso(today, 21),
    passengers: DEFAULT_PASSENGERS,
    cabin: 'ECONOMY',
  }
}

export function maxDate(today: IsoDate) {
  return addDaysIso(today, MAX_DAYS_AHEAD)
}

/** El primer problema del borrador, en palabras; null si se puede buscar. */
export function draftProblem(draft: SearchDraft, today: IsoDate): string | null {
  if (!draft.origin) return 'Elegí desde dónde salís.'
  if (!draft.destination) return 'Elegí a dónde vas.'
  if (draft.origin.iata === draft.destination.iata) return 'El origen y el destino no pueden ser el mismo aeropuerto.'
  if (!draft.departureDate) return 'Elegí la fecha de ida.'
  if (draft.departureDate < today) return 'La fecha de ida ya pasó.'
  if (draft.departureDate > maxDate(today)) return `Las aerolíneas publican vuelos hasta ${MAX_DAYS_AHEAD} días antes.`
  if (draft.tripType === 'ROUND_TRIP') {
    if (!draft.returnDate) return 'Elegí la fecha de vuelta.'
    if (draft.returnDate < draft.departureDate) return 'La vuelta no puede ser anterior a la ida.'
  }
  if (totalPassengers(draft.passengers) > MAX_PASSENGERS) return `Se pueden buscar hasta ${MAX_PASSENGERS} pasajeros.`
  if (draft.passengers.infants > draft.passengers.adults) return 'No puede haber más infantes que adultos.'
  return null
}

/**
 * Invertir origen y destino sólo tiene sentido si el origen también puede ser destino: hoy el destino
 * tiene que estar en Bolivia, así que desde Madrid no se puede "dar vuelta" el viaje.
 */
export function canSwap(draft: SearchDraft) {
  return Boolean(draft.origin && draft.destination && draft.origin.countryCode === 'BO')
}

// ---------------------------------------------------------------- parámetros de la ruta de resultados

/** Lo que viaja en la URL de resultados. Cortito y plano: expo-router lo pasa como strings. */
export type SearchParams = {
  o: string
  d: string
  dep: string
  ret?: string
  ad: string
  ch?: string
  inf?: string
  cab: string
}

export function toSearchParams(draft: SearchDraft): SearchParams {
  return {
    o: draft.origin?.iata ?? '',
    d: draft.destination?.iata ?? '',
    dep: draft.departureDate ?? '',
    ...(draft.tripType === 'ROUND_TRIP' && draft.returnDate ? { ret: draft.returnDate } : {}),
    ad: String(draft.passengers.adults),
    ...(draft.passengers.childrenAges.length > 0 ? { ch: draft.passengers.childrenAges.join(',') } : {}),
    ...(draft.passengers.infants > 0 ? { inf: String(draft.passengers.infants) } : {}),
    cab: draft.cabin,
  }
}

const ISO = /^\d{4}-\d{2}-\d{2}$/
const IATA = /^[A-Z]{3}$/

/**
 * De la URL al pedido. Devuelve null si algo no tiene forma válida: una URL editada a mano no puede llegar
 * al backend como cualquier cosa. Las reglas de negocio (destino en Bolivia, cupos) las valida el backend.
 */
export function requestFromParams(params: Partial<Record<keyof SearchParams, string | string[]>>): flightsApi.SearchFlightsRequest | null {
  const read = (key: keyof SearchParams) => {
    const value = params[key]
    return Array.isArray(value) ? value[0] : value
  }

  const origin = read('o')?.toUpperCase() ?? ''
  const destination = read('d')?.toUpperCase() ?? ''
  const departure = read('dep') ?? ''
  const back = read('ret')
  const adults = Number(read('ad') ?? '1')
  const children = (read('ch') ?? '').split(',').filter(Boolean).map(Number)
  const infants = Number(read('inf') ?? '0')
  const cabin = (read('cab') ?? 'ECONOMY') as Cabin

  if (!IATA.test(origin) || !IATA.test(destination) || !ISO.test(departure)) return null
  if (back !== undefined && !ISO.test(back)) return null
  if (![adults, infants, ...children].every(Number.isInteger)) return null
  if (!CABINS.some((c) => c.value === cabin)) return null

  return {
    originIata: origin,
    destinationIata: destination,
    departureDate: departure,
    ...(back ? { returnDate: back } : {}),
    adults,
    childrenAges: children,
    infants,
    cabinClass: cabin,
  }
}

// ---------------------------------------------------------------- calendario de ida y vuelta

export type DateField = 'departure' | 'return'

export interface DateRange {
  departure: IsoDate | null
  return: IsoDate | null
}

export type RangeDayState = 'disabled' | 'start' | 'end' | 'start-end' | 'in-range' | 'selectable'

/**
 * Cómo se ve un día. Deshabilitados: los pasados, los que están fuera del horizonte de venta de las
 * aerolíneas y —mientras se elige la vuelta— los anteriores a la ida. Así no hay forma de armar una vuelta
 * antes de la ida, en vez de avisarlo después.
 */
export function rangeDayState(
  iso: IsoDate,
  range: DateRange,
  editing: DateField,
  today: IsoDate,
): RangeDayState {
  if (iso < today || iso > maxDate(today)) return 'disabled'
  if (editing === 'return' && range.departure && iso < range.departure) return 'disabled'

  const isStart = iso === range.departure
  const isEnd = iso === range.return
  if (isStart && isEnd) return 'start-end'
  if (isStart) return 'start'
  if (isEnd) return 'end'
  if (range.departure && range.return && iso > range.departure && iso < range.return) return 'in-range'
  return 'selectable'
}

/**
 * Elegir un día. En ida y vuelta, elegir la ida pasa solo a elegir la vuelta (como en cualquier buscador de
 * vuelos), y una ida nueva posterior a la vuelta borra la vuelta en vez de dejar un viaje al revés.
 */
export function selectDay(
  range: DateRange,
  editing: DateField,
  iso: IsoDate,
  tripType: TripType,
): { range: DateRange; editing: DateField } {
  if (editing === 'departure' || tripType === 'ONE_WAY') {
    const keepReturn = range.return && range.return >= iso ? range.return : null
    return {
      range: { departure: iso, return: tripType === 'ROUND_TRIP' ? keepReturn : null },
      editing: tripType === 'ROUND_TRIP' ? 'return' : 'departure',
    }
  }

  if (range.departure && iso < range.departure) return { range: { departure: iso, return: null }, editing: 'return' }
  return { range: { ...range, return: iso }, editing: 'return' }
}

// ---------------------------------------------------------------- resultados: orden y filtros

type Option = flightsApi.FlightOptionResponse
type Slice = NonNullable<Option['slices']>[number]

export type SortKey = 'PRICE' | 'DURATION' | 'DEPARTURE'

export const SORTS: { value: SortKey; label: string }[] = [
  { value: 'PRICE', label: 'Más barato' },
  { value: 'DURATION', label: 'Más rápido' },
  { value: 'DEPARTURE', label: 'Sale antes' },
]

export type TimeBucket = 'EARLY' | 'MORNING' | 'AFTERNOON' | 'EVENING'

export const TIME_BUCKETS: { value: TimeBucket; label: string; from: number; to: number }[] = [
  { value: 'EARLY', label: 'Madrugada (0–6)', from: 0, to: 6 },
  { value: 'MORNING', label: 'Mañana (6–12)', from: 6, to: 12 },
  { value: 'AFTERNOON', label: 'Tarde (12–18)', from: 12, to: 18 },
  { value: 'EVENING', label: 'Noche (18–24)', from: 18, to: 24 },
]

export interface ResultFilters {
  /** null = cualquiera; 0 = sólo directos; 1 = hasta una escala. */
  maxStops: number | null
  departureTimes: TimeBucket[]
  withCheckedBag: boolean
  carriers: string[]
}

export const NO_FILTERS: ResultFilters = { maxStops: null, departureTimes: [], withCheckedBag: false, carriers: [] }

export function activeFilterCount(filters: ResultFilters) {
  return (
    (filters.maxStops === null ? 0 : 1) +
    (filters.departureTimes.length > 0 ? 1 : 0) +
    (filters.withCheckedBag ? 1 : 0) +
    (filters.carriers.length > 0 ? 1 : 0)
  )
}

function minutesOfDay(value?: string | null) {
  const time = value?.includes('T') ? value.split('T')[1] : value
  if (!time) return null
  const [hours, minutes] = time.split(':').map(Number)
  return hours * 60 + minutes
}

/** Duración de un tramo: la que informa el proveedor o, si no la informa, la que dan sus horarios. */
export function sliceMinutes(slice: Slice) {
  if (slice.durationMinutes) return slice.durationMinutes
  const segments = slice.segments ?? []
  const first = segments[0]?.departingAt
  const last = segments[segments.length - 1]?.arrivingAt
  if (!first || !last) return null
  return Math.round((new Date(last).getTime() - new Date(first).getTime()) / 60_000)
}

export function totalMinutes(option: Option) {
  return (option.slices ?? []).reduce((sum, slice) => sum + (sliceMinutes(slice) ?? 0), 0)
}

export function maxStopsOf(option: Option) {
  return Math.max(0, ...(option.slices ?? []).map((slice) => slice.stops ?? Math.max((slice.segments?.length ?? 1) - 1, 0)))
}

/**
 * Valijas despachadas incluidas en toda la oferta: el mínimo entre tramos (una valija en la ida y ninguna
 * en la vuelta no es "incluye valija"). null si algún tramo no lo informa: no se inventa.
 */
export function checkedBagsOf(option: Option): number | null {
  const bags = (option.slices ?? []).flatMap((slice) => (slice.segments ?? []).map((segment) => segment.checkedBags))
  if (bags.length === 0 || bags.some((value) => value === null || value === undefined)) return null
  return Math.min(...(bags as number[]))
}

export function departureMinutes(option: Option) {
  return minutesOfDay(option.slices?.[0]?.segments?.[0]?.departingAt)
}

export function carriersOf(options: Option[]) {
  return [...new Set(options.map((option) => option.carrierName).filter((name): name is string => Boolean(name)))].sort()
}

export function applyFilters(options: Option[], filters: ResultFilters) {
  return options.filter((option) => {
    if (filters.maxStops !== null && maxStopsOf(option) > filters.maxStops) return false
    if (filters.withCheckedBag && !((checkedBagsOf(option) ?? 0) > 0)) return false
    if (filters.carriers.length > 0 && !filters.carriers.includes(option.carrierName ?? '')) return false
    if (filters.departureTimes.length > 0) {
      const minutes = departureMinutes(option)
      if (minutes === null) return false
      const hour = minutes / 60
      if (!filters.departureTimes.some((key) => {
        const bucket = TIME_BUCKETS.find((b) => b.value === key)!
        return hour >= bucket.from && hour < bucket.to
      })) return false
    }
    return true
  })
}

export function sortOptions(options: Option[], sort: SortKey) {
  const price = (option: Option) => option.price?.amount ?? Number.POSITIVE_INFINITY
  const key: Record<SortKey, (option: Option) => number> = {
    PRICE: price,
    DURATION: (option) => totalMinutes(option) || Number.POSITIVE_INFINITY,
    DEPARTURE: (option) => departureMinutes(option) ?? Number.POSITIVE_INFINITY,
  }
  // El precio desempata siempre: entre dos igual de rápidos, el más barato primero.
  return [...options].sort((a, b) => key[sort](a) - key[sort](b) || price(a) - price(b))
}

/** "+1" cuando se llega al día siguiente (o más) de la salida, en horas locales de cada aeropuerto. */
export function arrivalDayOffset(departingAt?: string | null, arrivingAt?: string | null) {
  if (!departingAt || !arrivingAt) return 0
  const day = (value: string) => Date.UTC(+value.slice(0, 4), +value.slice(5, 7) - 1, +value.slice(8, 10))
  return Math.round((day(arrivingAt) - day(departingAt)) / 86_400_000)
}
