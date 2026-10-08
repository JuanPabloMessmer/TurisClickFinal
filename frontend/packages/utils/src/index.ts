/** Sin lógica de UI ni de red — helpers puros reutilizables entre Backoffice y Tourist Mobile. */

export function formatCurrency(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('es-BO', { style: 'currency', currency }).format(amount)
  } catch {
    // Currency desconocida para Intl (no debería pasar con la lista curada del backend) — fallback simple.
    return `${amount.toFixed(2)} ${currency}`
  }
}

/**
 * El número solo, con la separación de miles y decimales de Bolivia (`3.000,00`) y sin símbolo.
 *
 * Es para las tablas y tarjetas que ya dicen la moneda en el encabezado: repetirla en cada fila es ruido,
 * pero escribir `3000.00` al lado de un `Bs 3.000,00` hace dudar de si son el mismo importe.
 */
export function formatAmount(amount: number): string {
  return new Intl.NumberFormat('es-BO', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)
}

/**
 * Para un string "YYYY-MM-DD" puro (sin hora, ej. ExperienceAvailability.Date/ReservationItem.Date),
 * `new Date(value)` lo interpreta como medianoche UTC y al formatear en una zona horaria detrás de UTC
 * se muestra el día anterior. Se arma la fecha en horario local a partir de los componentes en vez de
 * dejar que el constructor de Date la interprete como UTC.
 */
export function formatDate(value: string | Date): string {
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const [year, month, day] = value.split('-').map(Number)
    return new Intl.DateTimeFormat('es-BO', { dateStyle: 'medium' }).format(new Date(year, month - 1, day))
  }
  const date = typeof value === 'string' ? new Date(value) : value
  return new Intl.DateTimeFormat('es-BO', { dateStyle: 'medium' }).format(date)
}

export function formatDateTime(value: string | Date): string {
  const date = typeof value === 'string' ? new Date(value) : value
  return new Intl.DateTimeFormat('es-BO', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

export * from './calendar'
