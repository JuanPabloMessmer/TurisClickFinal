import { Stack, useLocalSearchParams } from 'expo-router'
import { ScrollView, Text, View } from 'react-native'
import {
  AvailabilityRow,
  AvailabilitySection,
  BackButton,
  BookingBar,
  DetailBlock,
  DetailSkeleton,
} from '@/features/catalog/detail'
import { coverImageUrl } from '@/features/catalog/images'
import { useExperience, useExperienceAvailability } from '@/features/catalog/queries'
import { toApiError } from '@/lib/errors'
import { CatalogImage, Chip, ErrorState } from '@/ui'

/**
 * Detalle de una experiencia. Pantalla completa fuera de las tabs: la foto ocupa el tope con el botón
 * de volver flotando encima, que es lo que la hace sentir una app de viajes y no un formulario.
 */
export default function ExperienceDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()
  const experience = useExperience(id)
  const availability = useExperienceAvailability(id)

  return (
    <View className="flex-1 bg-background">
      <Stack.Screen options={{ headerShown: false }} />
      <BackButton />

      {experience.error ? (
        <View className="flex-1 justify-center">
          <ErrorState message={toApiError(experience.error).message} onRetry={experience.refetch} />
        </View>
      ) : experience.isPending ? (
        <DetailSkeleton />
      ) : (
        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 160 }}>
          <CatalogImage uri={coverImageUrl(experience.data.images)} className="h-72 w-full" />

          <View className="p-5">
            <Text className="text-xs font-medium uppercase tracking-wide text-secondary">
              {experience.data.destinationName}
            </Text>
            <Text className="mt-1 text-2xl font-bold text-ink">{experience.data.title}</Text>
            <Text className="mt-1 text-sm text-[#5B7285]">Operado por {experience.data.companyName}</Text>

            {experience.data.durationLabel ? (
              <View className="mt-4 flex-row">
                <Chip label={`⏱ ${experience.data.durationLabel}`} />
              </View>
            ) : null}

            {experience.data.description ? (
              <Text className="mt-5 text-base leading-6 text-ink">{experience.data.description}</Text>
            ) : null}

            {experience.data.includesText ? (
              <DetailBlock title="Qué incluye" body={experience.data.includesText} />
            ) : null}
            {experience.data.excludesText ? (
              <DetailBlock title="Qué no incluye" body={experience.data.excludesText} />
            ) : null}

            <AvailabilitySection isLoading={availability.isPending} slots={availability.data ?? []}>
              {availability.data?.map((slot) => (
                <AvailabilityRow
                  key={slot.id}
                  date={slot.date}
                  time={slot.startTime}
                  availableSlots={slot.availableSlots}
                />
              ))}
            </AvailabilitySection>
          </View>
        </ScrollView>
      )}

      {experience.data ? (
        <BookingBar
          amount={experience.data.price}
          currency={experience.data.currency}
          priceLabel="Precio por persona"
        />
      ) : null}
    </View>
  )
}
