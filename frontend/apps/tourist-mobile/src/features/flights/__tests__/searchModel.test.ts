import type { flightsApi } from '@turisclick/api-client'
import {
  applyFilters,
  arrivalDayOffset,
  canSwap,
  checkedBagsOf,
  draftProblem,
  initialDraft,
  NO_FILTERS,
  passengerLimits,
  passengersSummary,
  rangeDayState,
  requestFromParams,
  selectDay,
  sortOptions,
  toSearchParams,
  type AirportChoice,
  type SearchDraft,
} from '@/features/flights/search/model'

/**
 * Las reglas del buscador, sin pantalla. Son las mismas que valida el backend: si una de estas cambia sin
 * que cambie allá, la app dejaría buscar algo que el servidor rechaza (o al revés).
 */

const TODAY = '2026-10-19'

const MAD: AirportChoice = { iata: 'MAD', city: 'Madrid', name: 'Barajas', country: 'España', countryCode: 'ES' }
const VVI: AirportChoice = { iata: 'VVI', city: 'Santa Cruz de la Sierra', name: 'Viru Viru', country: 'Bolivia', countryCode: 'BO' }
const LPB: AirportChoice = { iata: 'LPB', city: 'La Paz', name: 'El Alto', country: 'Bolivia', countryCode: 'BO' }

const draft = (overrides: Partial<SearchDraft> = {}): SearchDraft => ({ ...initialDraft(TODAY), origin: MAD, destination: VVI, ...overrides })

describe('pasajeros', () => {
  it('hasta 9 en total, contando niños e infantes', () => {
    const full = passengerLimits({ adults: 4, childrenAges: [3, 5, 7, 9], infants: 1 })
    expect(full.full).toBe(true)
    expect(full.canAddAdult).toBe(false)
    expect(full.canAddChild).toBe(false)
    expect(full.canAddInfant).toBe(false)
  })

  it('no más infantes que adultos, y quitar un adulto no puede dejar a un infante sin brazos', () => {
    const limits = passengerLimits({ adults: 2, childrenAges: [], infants: 2 })
    expect(limits.canAddInfant).toBe(false)
    expect(limits.canRemoveAdult).toBe(false)
    expect(passengerLimits({ adults: 1, childrenAges: [], infants: 0 }).canRemoveAdult).toBe(false)
  })

  it('resume la composición en palabras', () => {
    expect(passengersSummary({ adults: 1, childrenAges: [], infants: 0 })).toBe('1 adulto')
    expect(passengersSummary({ adults: 2, childrenAges: [4], infants: 1 })).toBe('2 adultos, 1 niño, 1 infante')
  })
})

describe('borrador de búsqueda', () => {
  it('arranca en ida y vuelta, a dos y tres semanas', () => {
    const initial = initialDraft(TODAY)
    expect(initial.tripType).toBe('ROUND_TRIP')
    expect(initial.departureDate).toBe('2026-11-02')
    expect(initial.returnDate).toBe('2026-11-09')
  })

  it('dice qué falta o qué está mal, en orden', () => {
    expect(draftProblem(draft({ origin: null }), TODAY)).toMatch(/desde dónde/)
    expect(draftProblem(draft({ destination: null }), TODAY)).toMatch(/a dónde/)
    expect(draftProblem(draft({ destination: MAD }), TODAY)).toMatch(/mismo aeropuerto/)
    expect(draftProblem(draft({ departureDate: '2026-10-18' }), TODAY)).toMatch(/ya pasó/)
    expect(draftProblem(draft({ departureDate: '2027-12-01', returnDate: '2027-12-05' }), TODAY)).toMatch(/330 días/)
    expect(draftProblem(draft({ returnDate: null }), TODAY)).toMatch(/vuelta/)
    expect(draftProblem(draft({ departureDate: '2026-11-10', returnDate: '2026-11-05' }), TODAY)).toMatch(/anterior a la ida/)
    expect(draftProblem(draft({ tripType: 'ONE_WAY', returnDate: null }), TODAY)).toBeNull()
    expect(draftProblem(draft(), TODAY)).toBeNull()
  })

  it('sólo se puede invertir el viaje si el origen también puede ser destino (Bolivia)', () => {
    expect(canSwap(draft())).toBe(false)
    expect(canSwap(draft({ origin: LPB }))).toBe(true)
  })
})

describe('calendario de ida y vuelta', () => {
  it('deshabilita los pasados, los demasiado lejanos y, eligiendo la vuelta, los anteriores a la ida', () => {
    const range = { departure: '2026-11-02', return: '2026-11-09' }
    expect(rangeDayState('2026-10-18', range, 'departure', TODAY)).toBe('disabled')
    expect(rangeDayState('2027-09-20', range, 'departure', TODAY)).toBe('disabled')
    expect(rangeDayState('2026-11-01', range, 'return', TODAY)).toBe('disabled')
    expect(rangeDayState('2026-11-01', range, 'departure', TODAY)).toBe('selectable')
  })

  it('marca la ida, la vuelta y los días del medio', () => {
    const range = { departure: '2026-11-02', return: '2026-11-09' }
    expect(rangeDayState('2026-11-02', range, 'departure', TODAY)).toBe('start')
    expect(rangeDayState('2026-11-05', range, 'departure', TODAY)).toBe('in-range')
    expect(rangeDayState('2026-11-09', range, 'departure', TODAY)).toBe('end')
  })

  it('elegir la ida pasa solo a la vuelta, y una ida posterior a la vuelta la borra', () => {
    const step1 = selectDay({ departure: null, return: null }, 'departure', '2026-11-02', 'ROUND_TRIP')
    expect(step1).toEqual({ range: { departure: '2026-11-02', return: null }, editing: 'return' })

    const step2 = selectDay(step1.range, step1.editing, '2026-11-09', 'ROUND_TRIP')
    expect(step2.range).toEqual({ departure: '2026-11-02', return: '2026-11-09' })

    const later = selectDay(step2.range, 'departure', '2026-11-20', 'ROUND_TRIP')
    expect(later.range).toEqual({ departure: '2026-11-20', return: null })
  })

  it('en sólo ida no hay vuelta', () => {
    const oneWay = selectDay({ departure: '2026-11-02', return: '2026-11-09' }, 'departure', '2026-11-04', 'ONE_WAY')
    expect(oneWay).toEqual({ range: { departure: '2026-11-04', return: null }, editing: 'departure' })
  })
})

describe('parámetros de la búsqueda', () => {
  it('ida y vuelta: del borrador a la URL y de la URL al pedido', () => {
    const params = toSearchParams(
      draft({ passengers: { adults: 2, childrenAges: [4, 9], infants: 1 }, cabin: 'BUSINESS' }),
    )
    expect(requestFromParams(params)).toEqual({
      originIata: 'MAD',
      destinationIata: 'VVI',
      departureDate: '2026-11-02',
      returnDate: '2026-11-09',
      adults: 2,
      childrenAges: [4, 9],
      infants: 1,
      cabinClass: 'BUSINESS',
    })
  })

  it('sólo ida no manda vuelta', () => {
    const request = requestFromParams(toSearchParams(draft({ tripType: 'ONE_WAY' })))
    expect(request).not.toHaveProperty('returnDate')
  })

  it('una URL con forma inválida no llega al backend', () => {
    expect(requestFromParams({ o: 'MADRID', d: 'VVI', dep: '2026-11-02', ad: '1', cab: 'ECONOMY' })).toBeNull()
    expect(requestFromParams({ o: 'MAD', d: 'VVI', dep: 'mañana', ad: '1', cab: 'ECONOMY' })).toBeNull()
    expect(requestFromParams({ o: 'MAD', d: 'VVI', dep: '2026-11-02', ad: 'dos', cab: 'ECONOMY' })).toBeNull()
    expect(requestFromParams({ o: 'MAD', d: 'VVI', dep: '2026-11-02', ad: '1', cab: 'LUJO' })).toBeNull()
  })
})

type Option = flightsApi.FlightOptionResponse

const option = (
  price: number,
  departingAt: string,
  arrivingAt: string,
  { stops = 0, bags = 1 as number | null, carrier = 'Fake Airways' } = {},
): Option => ({
  price: { amount: price, currency: 'USD' },
  carrierName: carrier,
  slices: [
    {
      originIata: 'MAD',
      destinationIata: 'VVI',
      stops,
      segments: Array.from({ length: stops + 1 }, () => ({ departingAt, arrivingAt, checkedBags: bags })),
    },
  ],
})

describe('resultados: orden y filtros', () => {
  const cheapLate = option(300, '2026-11-02T20:15:00', '2026-11-03T06:00:00', { stops: 1, bags: 0 })
  const fastMorning = option(450, '2026-11-02T08:15:00', '2026-11-02T16:00:00')
  const midday = option(380, '2026-11-02T12:15:00', '2026-11-02T23:30:00', { bags: null, carrier: 'Otra Air' })
  const all = [fastMorning, cheapLate, midday]

  it('ordena por precio, duración y hora de salida', () => {
    expect(sortOptions(all, 'PRICE').map((o) => o.price?.amount)).toEqual([300, 380, 450])
    expect(sortOptions(all, 'DURATION')[0]).toBe(fastMorning)
    expect(sortOptions(all, 'DEPARTURE').map((o) => o.price?.amount)).toEqual([450, 380, 300])
  })

  it('filtra por escalas, horario, equipaje y aerolínea', () => {
    expect(applyFilters(all, { ...NO_FILTERS, maxStops: 0 })).toEqual([fastMorning, midday])
    expect(applyFilters(all, { ...NO_FILTERS, departureTimes: ['EVENING'] })).toEqual([cheapLate])
    // Sin dato de equipaje no se puede afirmar que lo incluye.
    expect(applyFilters(all, { ...NO_FILTERS, withCheckedBag: true })).toEqual([fastMorning])
    expect(applyFilters(all, { ...NO_FILTERS, carriers: ['Otra Air'] })).toEqual([midday])
  })

  it('el equipaje sin informar es null, no cero', () => {
    expect(checkedBagsOf(midday)).toBeNull()
    expect(checkedBagsOf(cheapLate)).toBe(0)
  })

  it('marca la llegada al día siguiente', () => {
    expect(arrivalDayOffset('2026-11-02T20:15:00', '2026-11-03T06:00:00')).toBe(1)
    expect(arrivalDayOffset('2026-11-02T08:15:00', '2026-11-02T16:00:00')).toBe(0)
  })
})
