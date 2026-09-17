import type { ItineraryItemResponse, ItineraryResponse, MessageResponse } from '@turisclick/api-client'
import { toApiError } from '@/lib/errors'

/** Reglas de presentación del asistente. Nada de esto decide productos, precios ni cupos: eso es del backend. */

/** Frases iniciales pensadas para lo que el asistente entiende (destino, días, intereses, viajeros). */
export const STARTER_PROMPTS = [
  'Voy 4 días a La Paz. Me gusta la naturaleza y la gastronomía.',
  'Armame un viaje para este fin de semana en Santa Cruz de la Sierra.',
  'Esta vez quiero algo tranquilo y cultural en Sucre, 3 días.',
  'Quiero conocer el Salar de Uyuni, 2 días, somos 2 personas.',
]

/** Ajustes rápidos sobre una propuesta existente. Usan las mismas palabras que interpreta el backend. */
export const REFINEMENTS = [
  { label: '💸 Más barato', message: 'Quiero algo más barato' },
  { label: '🧗 Menos aventura', message: 'Menos aventura' },
  { label: '🍲 Agregá gastronomía', message: 'Agregá gastronomía' },
  { label: '🔁 Cambiá el día 2', message: 'Cambiá el segundo día' },
  { label: '📦 Prefiero un paquete', message: 'Prefiero un paquete' },
]

/**
 * Respuestas rápidas para lo que falta. Las claves son los textos reales de `missingInformation` que
 * devuelve el backend (AiConversationService.ComputeMissingFields).
 */
export function quickRepliesFor(missing: string[], destinationNames: string[]): { label: string; message: string }[] {
  const replies: { label: string; message: string }[] = []
  if (missing.includes('destino')) {
    for (const name of destinationNames.slice(0, 4)) replies.push({ label: `📍 ${name}`, message: `Quiero ir a ${name}` })
  }
  if (missing.includes('fechas o duración del viaje')) {
    replies.push({ label: '📅 Este fin de semana', message: 'Este fin de semana' })
    replies.push({ label: '🗓️ 3 días', message: '3 días' })
    replies.push({ label: '🗓️ 5 días', message: '5 días' })
  }
  if (missing.includes('cantidad de viajeros')) {
    replies.push({ label: '🎒 Viajo solo/a', message: 'Viajo solo' })
    replies.push({ label: '💑 Somos 2', message: 'Somos 2 personas' })
    replies.push({ label: '👨‍👩‍👧 Somos 4', message: 'Somos 4 personas' })
  }
  return replies
}

export interface ItineraryDay {
  dayNumber: number
  items: ItineraryItemResponse[]
}

export function groupByDay(items?: ItineraryItemResponse[] | null): ItineraryDay[] {
  const byDay = new Map<number, ItineraryItemResponse[]>()
  for (const item of items ?? []) {
    const day = item.dayNumber ?? 1
    byDay.set(day, [...(byDay.get(day) ?? []), item])
  }
  return [...byDay.entries()]
    .sort(([a], [b]) => a - b)
    .map(([dayNumber, dayItems]) => ({ dayNumber, items: [...dayItems].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0)) }))
}

export function itineraryItemTitle(item: ItineraryItemResponse) {
  return item.experienceTitle ?? item.packageTitle ?? 'Componente'
}

export type Tone = 'success' | 'warning' | 'danger' | 'neutral'

/** `ItemAvailabilityState` del backend → texto y tono. */
export function availabilityBadge(item: ItineraryItemResponse): { label: string; tone: Tone } {
  switch (item.availabilityState) {
    case 'AVAILABLE': {
      const slots = item.currentAvailableSlots
      return { label: slots != null ? `Disponible · ${slots} ${slots === 1 ? 'lugar' : 'lugares'}` : 'Disponible', tone: 'success' }
    }
    case 'SOLD_OUT':
      return { label: 'Sin cupo', tone: 'danger' }
    case 'SLOT_CLOSED':
      return { label: 'Fecha cerrada', tone: 'danger' }
    case 'UNPUBLISHED':
    case 'PRODUCT_NOT_FOUND':
      return { label: 'Ya no disponible', tone: 'danger' }
    default:
      return { label: 'Sin confirmar', tone: 'neutral' }
  }
}

export function itineraryStatusLabel(status?: string | null): { label: string; tone: Tone } {
  switch (status) {
    case 'SAVED':
      return { label: 'Guardado', tone: 'success' }
    case 'BOOKED':
      return { label: 'Reservado', tone: 'success' }
    case 'DISCARDED':
      return { label: 'Descartado', tone: 'neutral' }
    default:
      return { label: 'Borrador', tone: 'warning' }
  }
}

export function canSave(itinerary?: ItineraryResponse | null) {
  return itinerary?.status === 'DRAFT'
}

export function canBook(itinerary?: ItineraryResponse | null) {
  return Boolean(itinerary && (itinerary.status === 'DRAFT' || itinerary.status === 'SAVED') && itinerary.isStillBookable && (itinerary.items?.length ?? 0) > 0)
}

/** Errores reales de POST /api/ai/itineraries/{id}/book (ErrorCodes del backend). */
export function describeBookingFailure(error: unknown): { message: string; checkTrips: boolean } {
  const apiError = toApiError(error)
  if (apiError.isNetworkError) {
    return {
      message: 'No pudimos confirmar si la reserva se creó. Revisá "Mis viajes" antes de volver a intentar.',
      checkTrips: true,
    }
  }
  switch (apiError.code) {
    case 'INSUFFICIENT_CAPACITY':
      return { message: 'Uno de los componentes ya no tiene cupo suficiente, así que no se reservó nada. Pedile al asistente un cambio.', checkTrips: false }
    case 'PRODUCT_UNAVAILABLE':
      return { message: 'Uno de los componentes ya no está publicado. Pedile al asistente que lo reemplace.', checkTrips: false }
    case 'ITINERARY_ALREADY_BOOKED':
      return { message: 'Este itinerario ya fue reservado. Lo encontrás en "Mis viajes".', checkTrips: true }
    case 'AVAILABILITY_NOT_RESOLVED':
      return { message: 'Algún componente no tiene una fecha concreta disponible. Pedile al asistente otra opción.', checkTrips: false }
    case 'ITINERARY_EMPTY':
      return { message: 'El itinerario no tiene componentes para reservar.', checkTrips: false }
    default:
      return { message: apiError.message, checkTrips: false }
  }
}

export const isAssistant = (message: MessageResponse) => message.sender === 'AI'

/** El último mensaje del asistente: debajo de él se muestra la propuesta vigente. */
export function lastAssistantIndex(messages: MessageResponse[]) {
  for (let i = messages.length - 1; i >= 0; i--) if (isAssistant(messages[i])) return i
  return -1
}
