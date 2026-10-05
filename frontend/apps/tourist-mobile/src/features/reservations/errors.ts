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

export type PayFailureKind =
  | 'EXPIRED'
  | 'STALE'
  | 'NOT_FOUND'
  | 'SESSION'
  | 'NETWORK'
  | 'UNKNOWN'
  /** El vuelo dejó de estar disponible o no se pudo emitir: hay que volver a armar la reserva. */
  | 'FLIGHT_GONE'
  /** La emisión quedó sin resolver: el backend la está reconciliando y reintentar duplicaría el pasaje. */
  | 'FLIGHT_IN_PROGRESS'
  /** No se pudo contactar a la aerolínea y nada salió: se puede reintentar tal cual. */
  | 'FLIGHT_RETRYABLE'

/**
 * Los códigos de vuelo se distinguen de los demás porque la acción que corresponde es distinta en cada
 * caso, y confundirlas es caro: reintentar cuando la emisión quedó en curso puede comprar dos pasajes.
 */
const FLIGHT_FAILURES: Record<string, Failure<PayFailureKind>> = {
  FLIGHT_BOOKING_IN_PROGRESS: {
    kind: 'FLIGHT_IN_PROGRESS',
    message:
      'Estamos confirmando tu vuelo con la aerolínea. No hace falta volver a intentar: vas a ver el resultado acá en unos minutos.',
  },
  FLIGHT_PROVIDER_UNREACHABLE: {
    kind: 'FLIGHT_RETRYABLE',
    message: 'No pudimos contactar a la aerolínea. Tu lugar sigue reservado: probá de nuevo en unos minutos.',
  },
  FLIGHT_BOOKING_FAILED: {
    kind: 'FLIGHT_GONE',
    message: 'La aerolínea no pudo emitir el pasaje. No se te cobró nada y liberamos la salida del paquete.',
  },
  FLIGHT_UNAVAILABLE: {
    kind: 'FLIGHT_GONE',
    message: 'Ese vuelo dejó de estar disponible. Volvé a buscar vuelos para armar la reserva con otra opción.',
  },
  FLIGHT_QUOTE_EXPIRED: {
    kind: 'FLIGHT_GONE',
    message: 'La cotización del vuelo venció. Volvé a buscar vuelos para ver el precio actual.',
  },
  FLIGHT_REQUIREMENTS_UNSUPPORTED: {
    kind: 'FLIGHT_GONE',
    message:
      'Ese vuelo exige documento de identidad de cada pasajero y todavía no lo pedimos en la app. Elegí otra opción de vuelo.',
  },
}

export function describePayFailure(error: unknown): Failure<PayFailureKind> {
  const apiError = toApiError(error)

  if (apiError.isNetworkError) {
    return {
      kind: 'NETWORK',
      message: 'No pudimos confirmar el pago por un problema de conexión. Estamos revisando el estado de tu reserva.',
    }
  }

  if (apiError.code && FLIGHT_FAILURES[apiError.code]) return FLIGHT_FAILURES[apiError.code]

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

  // Cancelar mientras se emite liberaría un cupo que puede estar comprado: el backend lo bloquea y acá se
  // explica por qué, en vez de mostrar un conflicto sin sentido.
  if (apiError.code === 'FLIGHT_BOOKING_IN_PROGRESS') {
    return {
      kind: 'NOT_CANCELLABLE',
      message: 'Estamos confirmando el vuelo de esta reserva. Vas a poder cancelarla en unos minutos.',
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
