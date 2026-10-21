import {
  ageOn,
  buildFlightReservationRequest,
  bookingParams,
  departureIdsFromParam,
  findSelectedOption,
  isBookableProviderOption,
  issuanceDisplay,
  passengerMixProblem,
  passengerSlots,
  samplePassengers,
  seatedCount,
  validatePassenger,
  type PassengerSlot,
  type TravelDates,
} from '@/features/flights/booking/model'
import { requestFromParams } from '@/features/flights/search/model'
import { airlineOption, BACK_ID, flightItem, OUT_ID, providerOption } from '@/test-utils/providerFlights'

/**
 * Reglas de la reserva de un vuelo de operador. Las de pasajeros son las del backend (B1): se repiten en la
 * app para no hacer corregir un dato después de reservar, así que si divergen, estos tests lo dicen.
 */

const ONE_WAY: TravelDates = { departure: '2026-11-20', return: null, today: '2026-10-21' }
const ROUND: TravelDates = { departure: '2026-11-20', return: '2027-03-10', today: '2026-10-21' }
const adult: PassengerSlot = { type: 'ADULT' }
const child: PassengerSlot = { type: 'CHILD', searchedAge: 7 }
const infant: PassengerSlot = { type: 'INFANT' }
const person = (bornOn: string, givenName = 'Ana', familyName = 'Prueba') => ({ givenName, familyName, bornOn })

describe('qué se puede reservar', () => {
  it('un vuelo de operador marcado reservable, con una o dos salidas', () => {
    expect(isBookableProviderOption(providerOption())).toBe(true)
    expect(isBookableProviderOption(providerOption(true))).toBe(true)
  })

  it('una oferta de aerolínea (proveedor simulado) nunca, aunque viniera marcada reservable', () => {
    expect(isBookableProviderOption(airlineOption())).toBe(false)
    expect(isBookableProviderOption(airlineOption({ bookable: true }))).toBe(false)
    // Un backend anterior a B1 no manda `source`: tampoco se ofrece.
    expect(isBookableProviderOption(airlineOption({ source: undefined, bookable: true }))).toBe(false)
  })

  it('un vuelo de operador sin cupo (bookable false) o sin salidas no se ofrece', () => {
    expect(isBookableProviderOption(providerOption(false, { bookable: false }))).toBe(false)
    expect(isBookableProviderOption(providerOption(false, { providerFlight: { departureIds: [] } }))).toBe(false)
  })

  it('reencuentra la opción elegida por sus salidas, y no si ya no está', () => {
    const options = [airlineOption(), providerOption(), providerOption(true)]
    expect(findSelectedOption(options, [OUT_ID, BACK_ID])).toBe(options[2])
    expect(findSelectedOption(options, [OUT_ID])).toBe(options[1])
    expect(findSelectedOption([airlineOption()], [OUT_ID])).toBeNull()
  })
})

describe('la URL de la reserva', () => {
  it('lleva la búsqueda tal cual y vuelve a dar el mismo pedido (misma caché que los resultados)', () => {
    const request = { originIata: 'VVI', destinationIata: 'UYU', departureDate: '2026-11-20', returnDate: '2026-11-24', adults: 2, childrenAges: [7], infants: 1, cabinClass: 'ECONOMY' }
    const params = bookingParams(request, providerOption(true))

    expect(params.ids).toBe(`${OUT_ID},${BACK_ID}`)
    expect(requestFromParams(params)).toEqual(request)
  })

  it('sólo ida es una salida; ida y vuelta, dos distintas con forma de id', () => {
    expect(departureIdsFromParam(OUT_ID, false)).toEqual([OUT_ID])
    expect(departureIdsFromParam(`${OUT_ID},${BACK_ID}`, true)).toEqual([OUT_ID, BACK_ID])
    expect(departureIdsFromParam(`${OUT_ID},${BACK_ID}`, false)).toBeNull()
    expect(departureIdsFromParam(OUT_ID, true)).toBeNull()
    expect(departureIdsFromParam(`${OUT_ID},${OUT_ID}`, true)).toBeNull()
    expect(departureIdsFromParam('no-es-un-id', false)).toBeNull()
    expect(departureIdsFromParam(undefined, false)).toBeNull()
  })
})

describe('pasajeros: cantidades', () => {
  it('arma los lugares desde la búsqueda: adultos, niños con su edad, infantes', () => {
    expect(passengerSlots({ adults: 2, childrenAges: [5], infants: 1 })).toEqual([adult, adult, { type: 'CHILD', searchedAge: 5 }, infant])
  })

  it('al menos un adulto, no más infantes que adultos, hasta 9 en total', () => {
    expect(passengerMixProblem([adult, child, infant])).toBeNull()
    expect(passengerMixProblem([child])).toBe('Tiene que viajar al menos un adulto.')
    expect(passengerMixProblem([adult, infant, infant])).toMatch(/no puede haber más infantes que adultos/)
    expect(passengerMixProblem(Array.from({ length: 10 }, () => adult))).toMatch(/hasta 9 pasajeros/)
    expect(passengerMixProblem([])).toBe('Faltan los pasajeros.')
  })

  it('con asiento son adultos y niños: los infantes viajan en brazos', () => {
    expect(seatedCount([adult, adult, child, infant])).toBe(3)
  })
})

describe('pasajeros: edades el día del vuelo', () => {
  it('cuenta años cumplidos sin desplazarse por huso', () => {
    expect(ageOn('2014-11-20', '2026-11-20')).toBe(12)
    expect(ageOn('2014-11-21', '2026-11-20')).toBe(11)
  })

  it('adulto: 12 años o más al salir', () => {
    expect(validatePassenger(person('1990-05-14'), adult, ONE_WAY)).toEqual({})
    expect(validatePassenger(person('2014-11-20'), adult, ONE_WAY)).toEqual({})
    expect(validatePassenger(person('2014-11-21'), adult, ONE_WAY).bornOn).toMatch(/12 años o más el día del vuelo/)
  })

  it('niño: de 2 a 11 al salir', () => {
    expect(validatePassenger(person('2019-03-01'), child, ONE_WAY)).toEqual({})
    expect(validatePassenger(person('2025-01-01'), child, ONE_WAY).bornOn).toMatch(/de 2 a 11 años/)
    expect(validatePassenger(person('2010-01-01'), child, ONE_WAY).bornOn).toMatch(/de 2 a 11 años/)
  })

  it('infante: menor de 2 al salir', () => {
    expect(validatePassenger(person('2026-04-01'), infant, ONE_WAY)).toEqual({})
    expect(validatePassenger(person('2024-11-19'), infant, ONE_WAY).bornOn).toMatch(/menor de 2 años/)
  })

  it('la edad se mide el día de la ida, como en el backend (B2), también en ida y vuelta', () => {
    expect(validatePassenger(person('2025-01-15'), infant, ROUND)).toEqual({})
    expect(validatePassenger(person('2014-11-21'), adult, ROUND).bornOn).toMatch(/12 años o más/)
  })

  it('fechas imposibles, futuras o absurdas', () => {
    expect(validatePassenger(person('1990-02-31'), adult, ONE_WAY).bornOn).toMatch(/formato año-mes-día/)
    expect(validatePassenger(person('14/05/1990'), adult, ONE_WAY).bornOn).toMatch(/formato año-mes-día/)
    expect(validatePassenger(person('2026-12-01'), infant, ONE_WAY).bornOn).toMatch(/tiene que ser pasada/)
    expect(validatePassenger(person('1890-01-01'), adult, ONE_WAY).bornOn).toMatch(/Revisá/)
  })

  it('nombres: obligatorios y sólo letras, como valida el backend', () => {
    expect(validatePassenger(person('1990-05-14', '', ''), adult, ONE_WAY)).toEqual({
      givenName: 'Escribí un nombre.',
      familyName: 'Escribí un apellido.',
    })
    expect(validatePassenger(person('1990-05-14', 'R2D2', 'Prueba'), adult, ONE_WAY).givenName).toMatch(/Sólo letras/)
    expect(validatePassenger(person('1990-05-14', "María José", "O'Neil-Quispe"), adult, ONE_WAY)).toEqual({})
  })

  it('los datos de ejemplo son ficticios y cumplen las reglas, también a la vuelta', () => {
    const slots = [adult, adult, child, infant]
    const samples = samplePassengers(slots, ROUND)

    samples.forEach((sample, index) => expect(validatePassenger(sample, slots[index], ROUND)).toEqual({}))
    expect(samples.every((sample) => sample.familyName === 'Prueba')).toBe(true)
  })
})

describe('el pedido de reserva (contrato B1)', () => {
  const slots = [adult, child, infant]
  const drafts = [person(' 1990-05-14 ', ' Ana '), person('2019-03-01', 'Luis'), person('2026-04-01', 'Sofía')]

  it('sólo ida: una salida, pasajeros sin documento y travelers = pasajeros con asiento', () => {
    expect(buildFlightReservationRequest([OUT_ID], slots, drafts)).toEqual({
      flightDepartureIds: [OUT_ID],
      travelers: 2,
      passengers: [
        { type: 'ADULT', givenName: 'Ana', familyName: 'Prueba', bornOn: '1990-05-14' },
        { type: 'CHILD', givenName: 'Luis', familyName: 'Prueba', bornOn: '2019-03-01' },
        { type: 'INFANT', givenName: 'Sofía', familyName: 'Prueba', bornOn: '2026-04-01' },
      ],
    })
  })

  it('ida y vuelta: las dos salidas en orden ida → vuelta', () => {
    expect(buildFlightReservationRequest([OUT_ID, BACK_ID], slots, drafts).flightDepartureIds).toEqual([OUT_ID, BACK_ID])
  })
})

describe('estado de la emisión', () => {
  const flight = (issuanceStatus: string) => flightItem('OUT', { issuanceStatus }).flight!

  it('sin pagar todavía no hay nada que emitir', () => {
    expect(issuanceDisplay(flight('NOT_REQUIRED'), 'PENDING_PAYMENT')).toMatchObject({ kind: 'AWAITING_PAYMENT', label: 'Pendiente de pago' })
  })

  it('pagado: "emisión pendiente" por el operador, nunca "emitido"', () => {
    const shown = issuanceDisplay(flight('PENDING'), 'CONFIRMED')
    expect(shown).toMatchObject({ kind: 'PENDING', label: 'Emisión pendiente', tone: 'warning' })
    expect(shown.message).toBe('Pago confirmado · emisión pendiente por Salar Travel. Te avisamos acá cuando registre el pasaje.')
    expect(`${shown.label} ${shown.message}`).not.toMatch(/emitido/i)
  })

  it('"Pasaje emitido" sólo con ISSUED', () => {
    expect(issuanceDisplay(flight('ISSUED'), 'CONFIRMED')).toMatchObject({ kind: 'ISSUED', label: 'Pasaje emitido', message: 'Pasaje emitido por Salar Travel.' })
    for (const status of ['NOT_REQUIRED', 'PENDING', 'FAILED', 'ALGO_NUEVO']) {
      expect(issuanceDisplay(flight(status), 'CONFIRMED').label).not.toBe('Pasaje emitido')
    }
  })

  it('emisión fallida: lo dice con el reembolso total', () => {
    expect(issuanceDisplay(flight('FAILED'), 'CANCELLED')).toMatchObject({
      kind: 'FAILED',
      label: 'Emisión fallida · reembolso total',
      tone: 'danger',
    })
  })
})
