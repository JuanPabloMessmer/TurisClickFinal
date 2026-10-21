import { describe, expect, it } from 'vitest'
import { EMPTY_DEPARTURE, toDepartureRequest, toLocalIso, validateDeparture } from '../departureForm'
import { flightErrorKind, flightErrorMessage, NOT_AUTHORIZED_MESSAGE } from '../flightErrors'
import { EMPTY_FLIGHT, toFlightRequest, validateFlight } from '../flightForm'
import { formatLocalDateTime, issuanceLabel } from '../labels'
import { httpError, notAuthorized, notAvailable } from './testUtils'

describe('errores esperados de vuelos de operador', () => {
  it('el 501 FEATURE_NOT_AVAILABLE es un estado, con el mensaje del servidor', () => {
    expect(flightErrorKind(notAvailable())).toBe('not-available')
    expect(flightErrorMessage(notAvailable())).toBe('Los vuelos de operador todavía no están disponibles en este servidor.')
  })

  it('el 403 FLIGHT_SELLER_NOT_AUTHORIZED explica la habilitación', () => {
    expect(flightErrorKind(notAuthorized())).toBe('not-authorized')
    expect(flightErrorMessage(notAuthorized())).toBe(NOT_AUTHORIZED_MESSAGE)
  })

  it('un 409 común sigue siendo un error con su detalle', () => {
    const conflict = httpError(409, { detail: 'El vuelo tiene reservas: la ruta no se puede cambiar.' })
    expect(flightErrorKind(conflict)).toBe('other')
    expect(flightErrorMessage(conflict)).toBe('El vuelo tiene reservas: la ruta no se puede cambiar.')
  })
})

describe('formulario de vuelo', () => {
  const valid = {
    ...EMPTY_FLIGHT,
    originIata: 'MIA',
    destinationIata: 'VVI',
    carrierName: 'Boliviana de Aviación',
    carrierIata: 'OB',
    flightNumber: '712',
  }

  it('acepta un vuelo completo y arma el pedido del contrato', () => {
    expect(validateFlight(valid)).toEqual({})
    expect(toFlightRequest({ ...valid, fareRules: '  ' })).toMatchObject({
      originIata: 'MIA',
      destinationIata: 'VVI',
      carrierIata: 'OB',
      flightNumber: '712',
      cabinClass: 'ECONOMY',
      fareRules: null,
    })
  })

  it('rechaza con los mismos mensajes que el backend', () => {
    const errors = validateFlight({ ...valid, destinationIata: 'MIA', carrierIata: 'O', flightNumber: '71234' })
    expect(errors.destinationIata).toBe('El destino tiene que ser distinto del origen.')
    expect(errors.carrierIata).toBe('El código de aerolínea tiene dos letras o números, por ejemplo OB.')
    expect(errors.flightNumber).toBe('El número de vuelo son hasta cuatro dígitos, por ejemplo 712.')
  })
})

describe('formulario de salida', () => {
  const today = new Date(2026, 9, 21)
  const valid = {
    ...EMPTY_DEPARTURE,
    departureAt: '2026-11-10T08:30',
    arrivalAt: '2026-11-10T14:05',
    totalSeats: '40',
    adultPrice: '1200',
    childPrice: '900',
    infantPrice: '0',
    currency: 'BOB',
  }

  it('la hora viaja local, sin huso ni Z', () => {
    expect(toLocalIso('2026-11-10T08:30')).toBe('2026-11-10T08:30:00')
    const request = toDepartureRequest(valid)
    expect(request.departureAt).toBe('2026-11-10T08:30:00')
    expect(request.arrivalAt).toBe('2026-11-10T14:05:00')
    expect(request.departureAt).not.toMatch(/Z|[+-]\d{2}:\d{2}$/)
    expect(request).toMatchObject({ totalSeats: 40, adultPrice: 1200, childPrice: 900, infantPrice: 0, currency: 'BOB' })
  })

  it('valida llegada, pasado, horizonte, asientos y precios', () => {
    expect(validateDeparture(valid, today)).toEqual({})
    expect(validateDeparture({ ...valid, arrivalAt: '2026-11-10T08:00' }, today).arrivalAt).toBe(
      'La llegada tiene que ser posterior a la salida.',
    )
    expect(validateDeparture({ ...valid, departureAt: '2026-10-01T08:00', arrivalAt: '2026-10-01T10:00' }, today).departureAt).toBe(
      'La salida ya pasó.',
    )
    expect(validateDeparture({ ...valid, departureAt: '2027-12-01T08:00', arrivalAt: '2027-12-01T10:00' }, today).departureAt).toBe(
      'Las salidas se publican hasta 330 días antes.',
    )
    const bad = validateDeparture({ ...valid, totalSeats: '0', adultPrice: '-1' }, today)
    expect(bad.totalSeats).toBe('Los asientos van de 1 a 500.')
    expect(bad.adultPrice).toBeDefined()
  })
})

describe('palabras', () => {
  it('"Emitido" es sólo ISSUED; pendiente no promete un pasaje', () => {
    expect(issuanceLabel.ISSUED).toBe('Emitido')
    for (const status of ['NOT_REQUIRED', 'PENDING', 'FAILED']) expect(issuanceLabel[status]).not.toMatch(/emitido\b/i)
    expect(Object.values(issuanceLabel).join(' ')).not.toMatch(/Pasaje emitido/)
  })

  it('formatea la hora local sin convertir husos', () => {
    expect(formatLocalDateTime('2026-11-10T08:30:00')).toBe('10/11/2026 08:30')
  })
})
