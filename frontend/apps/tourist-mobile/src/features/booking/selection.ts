/**
 * Reglas de la selección previa a reservar. Son solo las del formulario: el backend vuelve a validar cupo
 * y precio al crear la reserva, y su respuesta es la autoridad.
 */

/** Una fecha (o salida) reservable, normalizada desde la disponibilidad de Experience o Package. */
export interface BookableSlot {
  id: string
  date?: string
  /** Solo experiencias; los paquetes no tienen horario. */
  time?: string | null
  availableSlots: number
}

/** Límite del DTO `CreateReservationRequest.Travelers` ([Range(1, 100)]). */
export const MAX_TRAVELERS_PER_RESERVATION = 100

export function maxTravelersFor(slot: BookableSlot | undefined): number {
  if (!slot) return MAX_TRAVELERS_PER_RESERVATION
  return Math.max(1, Math.min(slot.availableSlots, MAX_TRAVELERS_PER_RESERVATION))
}

export function clampTravelers(travelers: number, slot: BookableSlot | undefined): number {
  return Math.min(Math.max(1, Math.floor(travelers)), maxTravelersFor(slot))
}

/** Precio × viajeros, en la moneda del producto. Solo orientativo: el backend congela el precio al reservar. */
export function estimatedTotal(price: number | null | undefined, travelers: number): number | null {
  if (price == null) return null
  return Math.round(price * travelers * 100) / 100
}
