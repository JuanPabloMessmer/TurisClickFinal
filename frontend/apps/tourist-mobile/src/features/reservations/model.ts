import type {
  ReservationItemResponse,
  ReservationResponse,
  ReservationTotalResponse,
} from '@turisclick/api-client'

/**
 * Lectura de una ReservationResponse para mostrarla. Nada de esto decide reglas de negocio: el backend
 * es la autoridad sobre estados, precios, cupos y expiración. Acá solo se interpreta lo que ya devolvió.
 */

/** Cómo se presenta una reserva. Combina `status`, `expiresAt` y el estado de sus ítems. */
export type ReservationDisplayKind =
  | 'PENDING'
  /** PENDING_PAYMENT con ExpiresAt vencido: el backend todavía no corrió la expiración (job cada ~60s). */
  | 'PENDING_TIME_UP'
  | 'CONFIRMED'
  | 'CONFIRMED_WITH_OPERATOR_CANCELLATIONS'
  | 'CANCELLED_BY_OPERATOR'
  | 'EXPIRED'
  | 'CANCELLED'
  /** PAYMENT_FAILED (existe en el enum pero nada lo escribe) o cualquier valor futuro. */
  | 'UNKNOWN'

export type StatusTone = 'warning' | 'success' | 'danger' | 'neutral'

export interface ReservationDisplayStatus {
  kind: ReservationDisplayKind
  label: string
  tone: StatusTone
}

export function isTimeUp(expiresAt: string | null | undefined, now: number): boolean {
  if (!expiresAt) return false
  const expires = new Date(expiresAt).getTime()
  return Number.isFinite(expires) && expires <= now
}

/**
 * Una línea cancelada por el PROVIDER (UC-P-14). El motivo es obligatorio en ese endpoint, y es el único
 * camino que lo escribe; además, si la reserva padre NO está cancelada, la línea no la canceló el turista
 * (él solo puede cancelar la reserva completa).
 */
export function isCancelledByOperator(item: ReservationItemResponse, reservationStatus?: string | null): boolean {
  if (item.status !== 'CANCELLED') return false
  return Boolean(item.cancellationReason?.trim()) || reservationStatus !== 'CANCELLED'
}

export function displayStatus(reservation: ReservationResponse, now: number): ReservationDisplayStatus {
  switch (reservation.status) {
    case 'PENDING_PAYMENT':
      return isTimeUp(reservation.expiresAt, now)
        ? { kind: 'PENDING_TIME_UP', label: 'Tiempo para pagar vencido', tone: 'neutral' }
        : { kind: 'PENDING', label: 'Pendiente de pago', tone: 'warning' }

    case 'CONFIRMED': {
      const items = reservation.items ?? []
      const cancelled = items.filter((item) => isCancelledByOperator(item, reservation.status)).length

      if (items.length > 0 && cancelled === items.length) {
        return { kind: 'CANCELLED_BY_OPERATOR', label: 'Cancelada por el operador', tone: 'danger' }
      }
      if (cancelled > 0) {
        return {
          kind: 'CONFIRMED_WITH_OPERATOR_CANCELLATIONS',
          label: `Confirmada · ${cancelled} ${cancelled === 1 ? 'servicio cancelado' : 'servicios cancelados'} por el operador`,
          tone: 'warning',
        }
      }
      return { kind: 'CONFIRMED', label: 'Confirmada', tone: 'success' }
    }

    case 'EXPIRED':
      return { kind: 'EXPIRED', label: 'Expirada', tone: 'neutral' }

    case 'CANCELLED':
      return { kind: 'CANCELLED', label: 'Cancelada', tone: 'danger' }

    default:
      return { kind: 'UNKNOWN', label: 'Estado no disponible', tone: 'neutral' }
  }
}

/** Estado de UNA línea, en palabras. `null` cuando coincide con el de la reserva y no aporta nada. */
export function itemStatusLabel(item: ReservationItemResponse, reservationStatus?: string | null): string | null {
  if (isCancelledByOperator(item, reservationStatus)) return 'Cancelado por el operador'
  if (item.status === reservationStatus) return null

  switch (item.status) {
    case 'CONFIRMED':
      return 'Confirmado'
    case 'PENDING_PAYMENT':
      return 'Pendiente de pago'
    case 'EXPIRED':
      return 'Expirado'
    case 'CANCELLED':
      return 'Cancelado'
    default:
      return null
  }
}

export function itemTitle(item: ReservationItemResponse): string {
  return item.experienceTitle ?? item.packageTitle ?? 'Producto'
}

/** Título de la reserva en una tarjeta: el primer producto y cuántos más hay. */
export function reservationTitle(reservation: ReservationResponse): { title: string; extraCount: number } {
  const items = reservation.items ?? []
  if (items.length === 0) return { title: 'Reserva', extraCount: 0 }
  return { title: itemTitle(items[0]), extraCount: items.length - 1 }
}

/** Se puede intentar pagar. El backend vuelve a validarlo todo: esto solo evita ofrecer algo imposible. */
export function canPay(reservation: ReservationResponse, now: number): boolean {
  return reservation.status === 'PENDING_PAYMENT' && !isTimeUp(reservation.expiresAt, now)
}

/**
 * El turista solo puede cancelar PENDING_PAYMENT (UC-T-11). Una CONFIRMED responde 409
 * REFUND_POLICY_REQUIRED, así que ni se ofrece.
 */
export function canCancel(reservation: ReservationResponse): boolean {
  return reservation.status === 'PENDING_PAYMENT'
}

export function secondsLeft(expiresAt: string, now: number): number {
  const expires = new Date(expiresAt).getTime()
  if (!Number.isFinite(expires)) return 0
  return Math.max(0, Math.ceil((expires - now) / 1000))
}

/** 29:59, o 1:02:03 si alguna vez el hold supera la hora. */
export function formatCountdown(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds))
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  const pad = (value: number) => value.toString().padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`
}

export interface PriceChangeLine {
  itemId: string
  title: string
  travelers: number
  previousUnitPrice: number
  previousCurrency: string
  currentUnitPrice: number
  currentCurrency: string
}

/** Las líneas que cambiaron de precio o de moneda en una respuesta de Pay con `requiresPriceAcceptance`. */
export function priceChangeLines(reservation: ReservationResponse): PriceChangeLine[] {
  return (reservation.items ?? [])
    .filter((item) => item.priceChanged)
    .map((item) => ({
      itemId: item.id ?? '',
      title: itemTitle(item),
      travelers: item.travelers ?? 0,
      previousUnitPrice: item.unitPrice ?? 0,
      previousCurrency: item.currency ?? '',
      currentUnitPrice: item.currentUnitPrice ?? item.unitPrice ?? 0,
      currentCurrency: item.currentCurrency ?? item.currency ?? '',
    }))
}

/**
 * Totales que quedarían si se acepta el precio vigente: `currentUnitPrice × travelers` en las líneas que
 * cambiaron, el snapshot en las demás, agrupado por moneda. Nunca convierte ni suma monedas distintas.
 * Es solo una vista previa: el importe real es el que devuelve el backend después de pagar.
 */
export function projectedTotals(reservation: ReservationResponse): Required<ReservationTotalResponse>[] {
  const byCurrency = new Map<string, number>()

  for (const item of reservation.items ?? []) {
    const changed = Boolean(item.priceChanged)
    const unitPrice = (changed ? item.currentUnitPrice : item.unitPrice) ?? item.unitPrice ?? 0
    const currency = (changed ? item.currentCurrency : item.currency) ?? item.currency ?? ''
    const subtotal = unitPrice * (item.travelers ?? 0)
    byCurrency.set(currency, (byCurrency.get(currency) ?? 0) + subtotal)
  }

  return [...byCurrency.entries()].map(([currency, amount]) => ({
    currency,
    amount: Math.round(amount * 100) / 100,
  }))
}

// ---- Expiración: re-lectura limitada ----

export const EXPIRY_POLL_INTERVAL_MS = 30_000
export const EXPIRY_POLL_MAX = 4

export interface ExpiryPollTracker {
  polls: number
  lastSeenUpdatedAt?: number
}

/**
 * Cuándo volver a leer una reserva cuyo tiempo para pagar ya venció pero que el backend todavía devuelve
 * PENDING_PAYMENT (su job de expiración corre cada ~60s). Cada 30s, como máximo 4 veces, y se corta apenas
 * el estado cambia.
 *
 * Cuenta lecturas reales (`dataUpdatedAt` distinto), no invocaciones: TanStack evalúa `refetchInterval`
 * también en cada render, y la cuenta regresiva re-renderiza cada segundo.
 */
export function nextExpiryPoll(
  state: { data?: ReservationResponse; dataUpdatedAt: number },
  now: number,
  tracker: ExpiryPollTracker,
): { delay: number | false; tracker: ExpiryPollTracker } {
  const waiting = state.data?.status === 'PENDING_PAYMENT' && isTimeUp(state.data.expiresAt, now)
  if (!waiting) return { delay: false, tracker: { polls: 0 } }

  const next =
    state.dataUpdatedAt !== tracker.lastSeenUpdatedAt
      ? { polls: tracker.polls + 1, lastSeenUpdatedAt: state.dataUpdatedAt }
      : tracker

  // La primera lectura observada ya vencida es la re-lectura inmediata; después vienen hasta 4 polls.
  return { delay: next.polls > EXPIRY_POLL_MAX ? false : EXPIRY_POLL_INTERVAL_MS, tracker: next }
}
