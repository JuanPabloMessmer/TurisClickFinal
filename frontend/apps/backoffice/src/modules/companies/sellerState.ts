import type { CompanyResponse } from '@turisclick/api-client'

/**
 * Los campos de vendedor de vuelos que el backend agrega a CompanyResponse (Workstream B2). Se leen como
 * opcionales: hasta que estén, la tarjeta muestra el estado que devuelve la última autorización.
 */
export type SellerFields = { canSellFlights?: boolean; flightSellerRegistration?: string | null; flightSellerAuthorizedAt?: string | null }

export function readSellerState(company: CompanyResponse): SellerFields {
  return company as CompanyResponse & SellerFields
}
