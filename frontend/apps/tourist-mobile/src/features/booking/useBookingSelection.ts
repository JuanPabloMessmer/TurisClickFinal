import { useEffect, useState } from 'react'
import { clampTravelers, maxTravelersFor, type BookableSlot } from '@/features/booking/selection'

/**
 * Estado de la selección (fecha + viajeros). Vive en la pantalla de reserva: cuando el invitado pasa por
 * login, el modal se abre ENCIMA de esta pantalla y al cerrarse vuelve a ella con la selección intacta.
 */
export function useBookingSelection(slots: BookableSlot[], slotsLoaded: boolean) {
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [requestedTravelers, setRequestedTravelers] = useState(1)

  const selected = slots.find((slot) => slot.id === selectedId)
  const max = maxTravelersFor(selected)
  // Si la disponibilidad se refrescó y queda menos cupo, se ajusta a lo posible sin perder la elección.
  const travelers = clampTravelers(requestedTravelers, selected)

  // La fecha elegida desapareció (se llenó o se cerró): se descarta en vez de reservar algo inexistente.
  useEffect(() => {
    if (slotsLoaded && selectedId && !selected) setSelectedId(null)
  }, [slotsLoaded, selectedId, selected])

  return {
    selected,
    selectedId,
    travelers,
    maxTravelers: max,
    canIncrement: travelers < max,
    canDecrement: travelers > 1,
    select: (id: string) => {
      setSelectedId(id)
      setRequestedTravelers((current) => clampTravelers(current, slots.find((slot) => slot.id === id)))
    },
    increment: () => setRequestedTravelers(Math.min(travelers + 1, max)),
    decrement: () => setRequestedTravelers(Math.max(1, travelers - 1)),
    clearSelection: () => setSelectedId(null),
  }
}
