import { Stack, useLocalSearchParams, type Href } from 'expo-router'
import { useMemo } from 'react'
import { BookingScreen } from '@/features/booking/BookingScreen'
import type { BookableSlot } from '@/features/booking/selection'
import { usePackage, usePackageAvailability } from '@/features/catalog/queries'

/** Elegir salida y viajeros de un paquete (UC-T-09). */
export default function BookPackageScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()
  const pkg = usePackage(id)
  const availability = usePackageAvailability(id)

  const slots = useMemo<BookableSlot[]>(
    () =>
      (availability.data ?? []).flatMap((slot) =>
        slot.id ? [{ id: slot.id, date: slot.departureDate, availableSlots: slot.availableSlots ?? 0 }] : [],
      ),
    [availability.data],
  )

  return (
    <>
      <Stack.Screen options={{ headerShown: false }} />
      <BookingScreen
        productType="PACKAGE"
        productHref={`/package/${id}` as Href}
        product={pkg.data}
        productQuery={pkg}
        slots={slots}
        availabilityQuery={availability}
      />
    </>
  )
}
