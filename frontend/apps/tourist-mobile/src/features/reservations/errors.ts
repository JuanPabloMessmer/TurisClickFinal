import { toApiError } from '@/lib/errors'

/**
 * Traducción de los errores REALES de cada endpoint de reservas a algo que la pantalla sepa manejar.
 * Los mensajes son propios: el backend no manda `errorCode` en la mayoría de estos casos (crear reserva
 * no manda ninguno), así que se decide por status y, cuando existe, por código.
 */

export type CreateReservationFailureKind =
  | 'NO_CAPACITY'
  | 'SLOT_UNAVAILABLE'
  | 'PRODUCT_UNAVAILABLE'
  | 'SESSION'
  | 'FORBIDDEN'
  | 'INVALID'
  | 'NETWORK'
  | 'UNKNOWN'

export interface Failure<K extends string> {
  kind: K
  message: string
}

export function describeCreateFailure(error: unknown, travelers: number): Failure<CreateReservationFailureKind> {
  const apiError = toApiError(error)

  if (apiError.isNetworkError) {
    // POST /api/reservations no tiene idempotencia: si la request llegó y se cayó la respuesta, la
    // reserva puede existir y estar reteniendo cupo. Por eso se manda a revisar antes de reintentar.
    return {
      kind: 'NETWORK',
      message:
        'No pudimos confirmar si la reserva se creó. Revisá "Mis viajes" antes de volver a intentar: si aparece pendiente, podés pagarla o cancelarla desde ahí.',
    }
  }

  switch (apiError.status) {
    case 409:
      return {
        kind: 'NO_CAPACITY',
        message: `Ya no quedan lugares suficientes para ${travelers} ${travelers === 1 ? 'viajero' : 'viajeros'} en esa fecha. Probá con menos viajeros u otra fecha.`,
      }
    case 410:
      return { kind: 'SLOT_UNAVAILABLE', message: 'Esa fecha ya no está disponible. Elegí otra.' }
    case 404:
      return { kind: 'PRODUCT_UNAVAILABLE', message: 'Este producto ya no está disponible para reservar.' }
    case 401:
      return { kind: 'SESSION', message: 'Tu sesión expiró. Iniciá sesión de nuevo para reservar.' }
    case 403:
      return { kind: 'FORBIDDEN', message: 'Solo las cuentas de turista pueden reservar.' }
    case 400:
      return { kind: 'INVALID', message: apiError.message }
    default:
      return { kind: 'UNKNOWN', message: apiError.message }
  }
}

export type PayFailureKind = 'EXPIRED' | 'STALE' | 'NOT_FOUND' | 'SESSION' | 'NETWORK' | 'UNKNOWN'

export function describePayFailure(error: unknown): Failure<PayFailureKind> {
  const apiError = toApiError(error)

  if (apiError.isNetworkError) {
    return {
      kind: 'NETWORK',
      message: 'No pudimos confirmar el pago por un problema de conexión. Estamos revisando el estado de tu reserva.',
    }
  }

  switch (apiError.status) {
    // Dos formas reales: con RESERVATION_NO_LONGER_PAYABLE (ya EXPIRED o carrera perdida) y SIN código
    // (PENDING_PAYMENT con ExpiresAt vencido antes de que corra el job). Para la persona es lo mismo.
    case 410:
      return { kind: 'EXPIRED', message: 'Se venció el tiempo para pagar esta reserva.' }
    // "La reserva está en estado X; no admite pago." — sin código. Puede ser un doble toque que ya la
    // confirmó: se vuelve a leer y la pantalla muestra el estado real.
    case 409:
      return { kind: 'STALE', message: 'La reserva cambió de estado. Actualizamos la información.' }
    case 404:
    case 403:
      return { kind: 'NOT_FOUND', message: 'No encontramos esta reserva.' }
    case 401:
      return { kind: 'SESSION', message: 'Tu sesión expiró. Iniciá sesión de nuevo para pagar.' }
    default:
      return { kind: 'UNKNOWN', message: apiError.message }
  }
}

export type CancelFailureKind =
  | 'REFUND_POLICY_REQUIRED'
  | 'NOT_CANCELLABLE'
  | 'NOT_FOUND'
  | 'SESSION'
  | 'NETWORK'
  | 'UNKNOWN'

export function describeCancelFailure(error: unknown): Failure<CancelFailureKind> {
  const apiError = toApiError(error)

  if (apiError.isNetworkError) {
    return { kind: 'NETWORK', message: 'No pudimos cancelar por un problema de conexión. Intentá de nuevo.' }
  }

  if (apiError.code === 'REFUND_POLICY_REQUIRED') {
    return {
      kind: 'REFUND_POLICY_REQUIRED',
      message: 'Las reservas confirmadas todavía no se pueden cancelar desde la app.',
    }
  }

  switch (apiError.status) {
    case 409:
      return { kind: 'NOT_CANCELLABLE', message: 'Esta reserva ya no se puede cancelar. Actualizamos su estado.' }
    case 404:
    case 403:
      return { kind: 'NOT_FOUND', message: 'No encontramos esta reserva.' }
    case 401:
      return { kind: 'SESSION', message: 'Tu sesión expiró. Iniciá sesión de nuevo.' }
    default:
      return { kind: 'UNKNOWN', message: apiError.message }
  }
}

/** Para el detalle: 403 (reserva ajena) y 404 se muestran igual, sin revelar que existe. */
export function isReservationNotFound(error: unknown): boolean {
  const { status } = toApiError(error)
  return status === 403 || status === 404
}
