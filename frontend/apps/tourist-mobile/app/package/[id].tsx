import type { PackageItemResponse } from '@turisclick/api-client'
import { Stack, useLocalSearchParams } from 'expo-router'
import { ScrollView, Text, View } from 'react-native'
import {
  AvailabilityRow,
  AvailabilitySection,
  BackButton,
  BookingBar,
  DetailBlock,
  DetailSkeleton,
  Gallery,
  TagRow,
} from '@/features/catalog/detail'
import { coverImageUrl } from '@/features/catalog/images'
import { usePackage, usePackageAvailability } from '@/features/catalog/queries'
import { toApiError } from '@/lib/errors'
import { CatalogImage, ErrorState } from '@/ui'

/**
 * Detalle de un paquete (UC-T-07). Misma anatomía que el de experiencia, más el itinerario: un paquete
 * es justamente una secuencia de días, y esconderla dejaría al turista sin saber qué está comprando.
 */
export default function PackageDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()
  const pkg = usePackage(id)
  const availability = usePackageAvailability(id)

  return (
    <View className="flex-1 bg-background">
      <Stack.Screen options={{ headerShown: false }} />
      <BackButton />

      {pkg.error ? (
        <View className="flex-1 justify-center">
          <ErrorState message={toApiError(pkg.error).message} onRetry={pkg.refetch} />
        </View>
      ) : pkg.isPending ? (
        <DetailSkeleton />
      ) : (
        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 180 }}>
          <CatalogImage uri={coverImageUrl(pkg.data.images)} className="h-72 w-full" />

          <View className="p-5">
            <Text className="text-xs font-medium uppercase tracking-wide text-secondary">
              {pkg.data.destinationName}
            </Text>
            <Text className="mt-1 text-2xl font-bold text-ink">{pkg.data.title}</Text>
            <Text className="mt-1 text-sm text-[#5B7285]">Operado por {pkg.data.companyName}</Text>

            <TagRow
              tags={[
                `🗓 ${pkg.data.durationDays} ${pkg.data.durationDays === 1 ? 'día' : 'días'}`,
                ...(pkg.data.categories ?? []).map((category) => category.name),
              ]}
            />

            {pkg.data.description ? (
              <Text className="mt-5 text-base leading-6 text-ink">{pkg.data.description}</Text>
            ) : null}

            <Itinerary items={pkg.data.items} />

            {pkg.data.conditionsText ? <DetailBlock title="Condiciones" body={pkg.data.conditionsText} /> : null}

            <Gallery images={pkg.data.images} />

            <AvailabilitySection isLoading={availability.isPending} slots={availability.data ?? []}>
              {availability.data?.map((slot) => (
                <AvailabilityRow key={slot.id} date={slot.departureDate} availableSlots={slot.availableSlots} />
              ))}
            </AvailabilitySection>
          </View>
        </ScrollView>
      )}

      {pkg.data ? (
        <BookingBar amount={pkg.data.price} currency={pkg.data.currency} priceLabel="Precio por persona" />
      ) : null}
    </View>
  )
}

/** Itinerario agrupado por día, respetando el `sortOrder` que definió el operador dentro de cada uno. */
function Itinerary({ items }: { items?: PackageItemResponse[] | null }) {
  if (!items?.length) return null

  const days = new Map<number, PackageItemResponse[]>()
  for (const item of [...items].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))) {
    const day = item.dayNumber ?? 0
    days.set(day, [...(days.get(day) ?? []), item])
  }

  return (
    <View className="mt-8">
      <Text className="mb-3 text-lg font-bold text-ink">Itinerario</Text>
      <View className="gap-4">
        {[...days.entries()]
          .sort(([a], [b]) => a - b)
          .map(([day, dayItems]) => (
            <View key={day} className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }}>
              <Text className="text-sm font-bold text-primary">Día {day}</Text>
              <View className="mt-2 gap-3">
                {dayItems.map((item) => (
                  <View key={item.id}>
                    <Text className="text-base font-medium text-ink">{item.title}</Text>
                    {item.description ? (
                      <Text className="mt-0.5 text-sm leading-5 text-[#5B7285]">{item.description}</Text>
                    ) : null}
                  </View>
                ))}
              </View>
            </View>
          ))}
      </View>
    </View>
  )
}
