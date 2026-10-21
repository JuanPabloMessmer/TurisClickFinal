export const TICKET_MIN = 6
export const TICKET_MAX = 20
/** Las reglas de RecordIssuanceRequest: un número por pasajero (infantes incluidos), de 6 a 20 caracteres. */
export function validateTickets(tickets: string[]): string | null {
  if (tickets.some((t) => t.trim().length === 0)) return 'Cargá el número de pasaje de cada pasajero.'
  if (tickets.some((t) => t.trim().length < TICKET_MIN || t.trim().length > TICKET_MAX))
    return `Cada número de pasaje tiene entre ${TICKET_MIN} y ${TICKET_MAX} caracteres.`
  return null
}
