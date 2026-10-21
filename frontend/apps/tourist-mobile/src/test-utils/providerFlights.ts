import type { flightsApi, ReservationItemResponse, ReservationResponse } from '@turisclick/api-client'
import { itemFixture, reservationFixture } from '@/test-utils'

/**
 * Respuestas simuladas de los vuelos de operador (contrato B1), mientras B2 no exista. Las formas son las de
 * los tipos generados: si el contrato cambia, estos fixtures dejan de compilar.
 */

export const OUT_ID = '11111111-1111-4111-8111-111111111111'
export const BACK_ID = '22222222-2222-4222-8222-222222222222'

const segment = (from: string, to: string, departingAt: string, arrivingAt: string, flightNumber: string) => ({
  originIata: from,
  destinationIata: to,
  departingAt,
  arrivingAt,
  carrierIata: 'OB',
  carrierName: 'Boliviana de Aviación',
  flightNumber,
  checkedBags: 1,
})

/** Vuelo de operador reservable. Ida (un id) o ida y vuelta (dos). */
export function providerOption(roundTrip = false, overrides: Partial<flightsApi.FlightOptionResponse> = {}): flightsApi.FlightOptionResponse {
  return {
    price: { amount: roundTrip ? 1800 : 900, currency: 'BOB' },
    carrierName: 'Boliviana de Aviación',
    carrierIata: 'OB',
    source: 'PROVIDER',
    bookable: true,
    slices: [
      { originIata: 'VVI', destinationIata: 'UYU', durationMinutes: null, stops: 0, segments: [segment('VVI', 'UYU', '2026-11-20T08:15:00', '2026-11-20T09:30:00', '712')] },
      ...(roundTrip
        ? [{ originIata: 'UYU', destinationIata: 'VVI', durationMinutes: null, stops: 0, segments: [segment('UYU', 'VVI', '2026-11-24T17:00:00', '2026-11-24T18:15:00', '713')] }]
        : []),
    ],
    providerFlight: {
      departureIds: roundTrip ? [OUT_ID, BACK_ID] : [OUT_ID],
      operatorCompanyId: '33333333-3333-4333-8333-333333333333',
      operatorName: 'Salar Travel',
      seatsLeft: 4,
      cancellationPolicy: [],
      fareRules: 'Cambios con cargo hasta 24 h antes.',
    },
    ...overrides,
  }
}

/** Oferta de aerolínea del proveedor simulado: se muestra para comparar, nunca se reserva. */
export function airlineOption(overrides: Partial<flightsApi.FlightOptionResponse> = {}): flightsApi.FlightOptionResponse {
  return {
    price: { amount: 700, currency: 'BOB' },
    carrierName: 'Fake Airways',
    carrierIata: 'ZZ',
    source: 'AIRLINE',
    bookable: false,
    slices: [
      { originIata: 'VVI', destinationIata: 'UYU', durationMinutes: 80, stops: 0, segments: [segment('VVI', 'UYU', '2026-11-20T12:00:00', '2026-11-20T13:20:00', '100')] },
    ],
    ...overrides,
  }
}

export function searchResponse(
  options: flightsApi.FlightOptionResponse[],
  overrides: Partial<flightsApi.SearchFlightsResponse> = {},
): flightsApi.SearchFlightsResponse {
  return {
    originIata: 'VVI',
    originLabel: 'Santa Cruz de la Sierra (VVI)',
    destinationIata: 'UYU',
    destinationLabel: 'Uyuni (UYU)',
    departureDate: '2026-11-20',
    returnDate: null,
    passengers: 1,
    adults: 1,
    childrenAges: [],
    infants: 0,
    cabinClass: 'ECONOMY',
    inventoryMode: 'SIMULATED',
    testMode: true,
    bookable: options.some((o) => o.bookable),
    options,
    packages: [],
    notice: null,
    ...overrides,
  }
}

type FlightItem = NonNullable<ReservationItemResponse['flight']>

export function flightItem(
  leg: 'OUT' | 'BACK',
  flight: Partial<FlightItem> = {},
  item: Partial<ReservationItemResponse> = {},
): ReservationItemResponse {
  const out = leg === 'OUT'
  return itemFixture({
    id: out ? 'item-out' : 'item-back',
    productType: 'FLIGHT',
    experienceId: null,
    experienceTitle: null,
    companyName: 'Salar Travel',
    travelers: 2,
    unitPrice: 450,
    currency: 'BOB',
    subtotal: 900,
    date: null,
    startTime: null,
    flight: {
      providerFlightId: 'pf-1',
      departureId: out ? OUT_ID : BACK_ID,
      originIata: out ? 'VVI' : 'UYU',
      originLabel: out ? 'Santa Cruz de la Sierra (VVI)' : 'Uyuni (UYU)',
      destinationIata: out ? 'UYU' : 'VVI',
      destinationLabel: out ? 'Uyuni (UYU)' : 'Santa Cruz de la Sierra (VVI)',
      departureAt: out ? '2026-11-20T08:15:00' : '2026-11-24T17:00:00',
      arrivalAt: out ? '2026-11-20T09:30:00' : '2026-11-24T18:15:00',
      carrierName: 'Boliviana de Aviación',
      carrierIata: 'OB',
      flightNumber: out ? '712' : '713',
      cabinClass: 'ECONOMY',
      checkedBags: 1,
      carryOnBags: 1,
      operatorName: 'Salar Travel',
      issuanceStatus: 'NOT_REQUIRED',
      ticketNumbers: [],
      bookingReference: null,
      issuedAt: null,
      passengers: [
        { type: 'ADULT', givenName: 'Ana', familyName: 'Prueba', bornOn: '1990-05-14' },
        { type: 'CHILD', givenName: 'Luis', familyName: 'Prueba', bornOn: '2019-03-01' },
      ],
      demo: true,
      ...flight,
    },
    ...item,
  })
}

/** Una reserva de vuelo de operador en el estado pedido. La vuelta es opcional. */
export function flightReservation(
  status: 'PENDING_PAYMENT' | 'CONFIRMED',
  issuance: 'NOT_REQUIRED' | 'PENDING' | 'ISSUED' | 'FAILED',
  { roundTrip = false, ...flightOverrides }: Partial<FlightItem> & { roundTrip?: boolean } = {},
  overrides: Partial<ReservationResponse> = {},
): ReservationResponse {
  const itemStatus = issuance === 'FAILED' ? 'CANCELLED' : status
  const extra: Partial<ReservationItemResponse> =
    issuance === 'FAILED'
      ? { status: 'CANCELLED', cancellationReason: 'La aerolínea no confirmó los asientos.', refundedAmount: 900 }
      : { status: itemStatus }
  const legs = [flightItem('OUT', { issuanceStatus: issuance, ...flightOverrides }, extra)]
  if (roundTrip) legs.push(flightItem('BACK', { issuanceStatus: issuance, ...flightOverrides }, extra))

  return reservationFixture({
    id: 'r1',
    status,
    items: legs,
    totals: [{ currency: 'BOB', amount: 900 * legs.length }],
    ...(status === 'CONFIRMED' ? { paymentApproved: true, expiresAt: null } : {}),
    ...overrides,
  })
}
