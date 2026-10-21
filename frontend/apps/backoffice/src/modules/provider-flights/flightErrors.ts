import { providerFlightsApi } from '@turisclick/api-client'
import { isAxiosError } from 'axios'
import { getErrorMessage } from '@/lib/errors'

/**
 * Los dos errores de vuelos de operador que no son "algo salió mal":
 *
 * - **501 `FEATURE_NOT_AVAILABLE`**: el contrato existe pero la funcionalidad todavía no (hasta que se integre
 *   el backend de vuelos de operador). Se muestra como un estado de la pantalla, no como un fallo.
 * - **403 `FLIGHT_SELLER_NOT_AUTHORIZED`**: la empresa no está habilitada para vender vuelos. Tampoco es un
 *   error del operador: es una habilitación que da un administrador.
 */
export type FlightErrorKind = 'not-available' | 'not-authorized' | 'other'

function errorCodeOf(error: unknown): string | undefined {
  if (!isAxiosError(error)) return undefined
  return (error.response?.data as { errorCode?: string } | undefined)?.errorCode
}

export function flightErrorKind(error: unknown): FlightErrorKind {
  const code = errorCodeOf(error)
  if (code === providerFlightsApi.FEATURE_NOT_AVAILABLE || (isAxiosError(error) && error.response?.status === 501))
    return 'not-available'
  if (code === providerFlightsApi.FLIGHT_SELLER_NOT_AUTHORIZED) return 'not-authorized'
  return 'other'
}

export const NOT_AVAILABLE_FALLBACK =
  'La venta de vuelos de operador todavía no está habilitada en este servidor. Las pantallas ya están listas; ' +
  'vas a poder usarlas cuando se active.'

export const NOT_AUTHORIZED_MESSAGE =
  'Tu empresa todavía no está autorizada para vender vuelos. Un administrador de TurisClick tiene que ' +
  'habilitarla con el registro o la licencia que declares.'

/** El mensaje para mostrar: el del servidor cuando lo hay (el 501 trae uno pensado para la pantalla). */
export function flightErrorMessage(error: unknown): string {
  const kind = flightErrorKind(error)
  if (kind === 'not-authorized') return NOT_AUTHORIZED_MESSAGE
  if (kind === 'not-available') {
    const detail = isAxiosError(error) ? (error.response?.data as { detail?: string } | undefined)?.detail : undefined
    return detail ?? NOT_AVAILABLE_FALLBACK
  }
  return getErrorMessage(error)
}
