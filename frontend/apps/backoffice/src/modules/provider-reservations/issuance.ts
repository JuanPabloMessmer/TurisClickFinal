export const TICKET_MIN = 6
export const TICKET_MAX = 20
/** Las reglas de RecordIssuanceRequest: un número por pasajero (infantes incluidos), de 6 a 20 caracteres. */
export function validateTickets(tickets: string[]): string | null {
  if (tickets.some((t) => t.trim().length === 0)) return 'Cargá el número de pasaje de cada pasajero.'
  if (tickets.some((t) => t.trim().length < TICKET_MIN || t.trim().length > TICKET_MAX))
    return `Cada número de pasaje tiene entre ${TICKET_MIN} y ${TICKET_MAX} caracteres.`
  return null
}

export const BOOKING_REFERENCE_MAX = 8
/** El localizador es opcional; si se carga, son 5 a 8 letras o números (el servidor lo pasa a mayúsculas). */
export function validateBookingReference(reference: string): string | null {
  const trimmed = reference.trim()
  if (trimmed && !/^[A-Za-z0-9]{5,8}$/.test(trimmed)) return 'El localizador son de 5 a 8 letras o números, por ejemplo ABC123.'
  return null
}
