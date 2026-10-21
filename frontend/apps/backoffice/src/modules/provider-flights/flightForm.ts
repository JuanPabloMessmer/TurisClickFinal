import type { CancellationTierDto, providerFlightsApi } from '@turisclick/api-client'
import { validate as validatePolicy } from '@/modules/packages/CancellationPolicySection'

export interface FlightFormValues {
  originIata: string
  originLabel: string
  destinationIata: string
  destinationLabel: string
  carrierName: string
  carrierIata: string
  flightNumber: string
  cabinClass: string
  checkedBags: number
  carryOnBags: number
  cancellationPolicy: CancellationTierDto[]
  fareRules: string
}

export const EMPTY_FLIGHT: FlightFormValues = {
  originIata: '',
  originLabel: '',
  destinationIata: '',
  destinationLabel: '',
  carrierName: '',
  carrierIata: '',
  flightNumber: '',
  cabinClass: 'ECONOMY',
  checkedBags: 0,
  carryOnBags: 1,
  cancellationPolicy: [],
  fareRules: '',
}

export function fromFlight(f: providerFlightsApi.ProviderFlightResponse): FlightFormValues {
  return {
    originIata: f.originIata ?? '',
    originLabel: f.originLabel ?? '',
    destinationIata: f.destinationIata ?? '',
    destinationLabel: f.destinationLabel ?? '',
    carrierName: f.carrierName ?? '',
    carrierIata: f.carrierIata ?? '',
    flightNumber: f.flightNumber ?? '',
    cabinClass: f.cabinClass ?? 'ECONOMY',
    checkedBags: f.checkedBags ?? 0,
    carryOnBags: f.carryOnBags ?? 1,
    cancellationPolicy: f.cancellationPolicy ?? [],
    fareRules: f.fareRules ?? '',
  }
}

/** Las mismas reglas que valida ProviderFlightRequest en el backend, con sus mismos mensajes. */
export function validateFlight(v: FlightFormValues): Partial<Record<keyof FlightFormValues, string>> {
  const errors: Partial<Record<keyof FlightFormValues, string>> = {}
  if (v.originIata.length !== 3) errors.originIata = 'Elegí el aeropuerto de origen.'
  if (v.destinationIata.length !== 3) errors.destinationIata = 'Elegí el aeropuerto de destino en Bolivia.'
  if (v.originIata && v.originIata === v.destinationIata) errors.destinationIata = 'El destino tiene que ser distinto del origen.'
  if (v.carrierName.trim().length < 2) errors.carrierName = 'Indicá la aerolínea u operador del vuelo.'
  if (v.carrierIata && !/^[A-Z0-9]{2}$/.test(v.carrierIata))
    errors.carrierIata = 'El código de aerolínea tiene dos letras o números, por ejemplo OB.'
  if (!/^[0-9]{1,4}[A-Z]?$/.test(v.flightNumber)) errors.flightNumber = 'El número de vuelo son hasta cuatro dígitos, por ejemplo 712.'
  if (v.fareRules.length > 2000) errors.fareRules = 'Las condiciones de la tarifa no pueden superar los 2000 caracteres.'
  if (validatePolicy(v.cancellationPolicy).length > 0) errors.cancellationPolicy = 'Revisá la política de cancelación.'
  return errors
}

export function toFlightRequest(v: FlightFormValues): providerFlightsApi.ProviderFlightRequest {
  return {
    originIata: v.originIata,
    destinationIata: v.destinationIata,
    carrierName: v.carrierName.trim(),
    carrierIata: v.carrierIata || null,
    flightNumber: v.flightNumber,
    cabinClass: v.cabinClass,
    checkedBags: v.checkedBags,
    carryOnBags: v.carryOnBags,
    cancellationPolicy: v.cancellationPolicy,
    fareRules: v.fareRules.trim() || null,
  }
}
