import { Stack, useLocalSearchParams, type Href } from 'expo-router'
import { useMemo } from 'react'
import { BookingScreen } from '@/features/booking/BookingScreen'
import type { BookableSlot } from '@/features/booking/selection'
import { useExperience, useExperienceAvailability } from '@/features/catalog/queries'

/** Elegir fecha, horario y viajeros de una experiencia (UC-T-08). */
export default function BookExperienceScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()
  const experience = useExperience(id)
  const availability = useExperienceAvailability(id)

  const slots = useMemo<BookableSlot[]>(
    () =>
      (availability.data ?? []).flatMap((slot) =>
        slot.id ? [{ id: slot.id, date: slot.date, time: slot.startTime, availableSlots: slot.availableSlots ?? 0 }] : [],
      ),
    [availability.data],
  )

  return (
    <>
      <Stack.Screen options={{ headerShown: false }} />
      <BookingScreen
        productType="EXPERIENCE"
        productHref={`/experience/${id}` as Href}
        product={experience.data}
        productQuery={experience}
        slots={slots}
        availabilityQuery={availability}
      />
    </>
  )
}
