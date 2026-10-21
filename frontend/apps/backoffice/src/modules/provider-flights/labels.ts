/**
 * Palabras de los vuelos de operador. Una sola fuente para que "emitido" no aparezca en ningún lado antes de
 * que el operador registre la emisión (docs/vuelos-sueltos-contratos.md, "Estados y palabras").
 */

type BadgeVariant = 'success' | 'warning' | 'destructive' | 'neutral'

export const flightStatusLabel: Record<string, string> = {
  DRAFT: 'Borrador',
  PUBLISHED: 'Publicado',
  ARCHIVED: 'Archivado',
}

export const flightStatusVariant: Record<string, BadgeVariant> = {
  DRAFT: 'warning',
  PUBLISHED: 'success',
  ARCHIVED: 'neutral',
}

export const departureStatusLabel: Record<string, string> = {
  OPEN: 'A la venta',
  CLOSED: 'Cerrada',
  CANCELLED: 'Cancelada',
}

export const departureStatusVariant: Record<string, BadgeVariant> = {
  OPEN: 'success',
  CLOSED: 'neutral',
  CANCELLED: 'destructive',
}

/** Lo que ve el operador. PENDING es una tarea suya, no un estado del pasaje. */
export const issuanceLabel: Record<string, string> = {
  NOT_REQUIRED: 'No requiere emisión',
  PENDING: 'Emisión pendiente',
  ISSUED: 'Emitido',
  FAILED: 'Emisión fallida',
}

export const issuanceVariant: Record<string, BadgeVariant> = {
  NOT_REQUIRED: 'neutral',
  PENDING: 'warning',
  ISSUED: 'success',
  FAILED: 'destructive',
}

export const cabinLabel: Record<string, string> = {
  ECONOMY: 'Económica',
  PREMIUM_ECONOMY: 'Económica premium',
  BUSINESS: 'Ejecutiva',
  FIRST: 'Primera',
}

export const CABIN_CLASSES = ['ECONOMY', 'PREMIUM_ECONOMY', 'BUSINESS', 'FIRST'] as const

export const passengerTypeLabel: Record<string, string> = {
  ADULT: 'Adulto',
  CHILD: 'Niño',
  INFANT: 'Infante',
}

/** "OB 712" o "712" si no hay código de aerolínea. */
export function flightCode(carrierIata?: string | null, flightNumber?: string | null): string {
  return [carrierIata, flightNumber].filter(Boolean).join(' ')
}

/** Hora local de la salida tal como la cargó el operador (el backend la guarda sin huso). */
export function formatLocalDateTime(value?: string | null): string {
  if (!value) return '—'
  const [date, time = ''] = value.split('T')
  const [y, m, d] = date.split('-')
  return `${d}/${m}/${y} ${time.slice(0, 5)}`.trim()
}
