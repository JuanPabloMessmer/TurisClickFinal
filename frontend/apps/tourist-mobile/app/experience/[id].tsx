import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router'
import { Clock } from 'lucide-react-native'
import { ScrollView, Text, View } from 'react-native'
import {
  AvailabilityRow,
  AvailabilitySection,
  BackButton,
  BookingBar,
  DETAIL_DATES_PREVIEW,
  DetailBlock,
  DetailSkeleton,
  Gallery,
  OperatorNote,
} from '@/features/catalog/detail'
import { coverImageUrl } from '@/features/catalog/images'
import { useExperience, useExperienceAvailability } from '@/features/catalog/queries'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { CatalogImage, ErrorState, Icon } from '@/ui'
import { Scrim } from '@/ui/Scrim'

/**
 * Detalle de una experiencia. Pantalla completa fuera de las tabs: la foto ocupa el tope con el nombre
 * del lugar encima, que es lo que la hace sentir una app de viajes y no un formulario.
 */
export default function ExperienceDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()
  const router = useRouter()
  const experience = useExperience(id)
  const availability = useExperienceAvailability(id)
  const noDates = availability.isSuccess && availability.data.length === 0

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
        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 170 }}>
          {/* El destino y el título viven SOBRE la foto: el lugar es el producto, y leerlo ahí evita
              que la ficha arranque con tres líneas de texto gris antes de mostrar a dónde se va. */}
          <View className="h-80 w-full bg-brand-900">
            <CatalogImage
              uri={coverImageUrl(experience.data.images)}
              className="absolute inset-0 h-full w-full"
              fallbackLabel={experience.data.destinationName}
            />
            <Scrim />
            <View className="absolute inset-x-0 bottom-0 p-4">
              <Text className="font-ui600 text-caption text-white/85">{experience.data.destinationName}</Text>
              <Text className="mt-1 font-display text-title text-white">{experience.data.title}</Text>
            </View>
          </View>

          <View className="p-4">
            {experience.data.durationLabel ? (
              <View className="flex-row items-center gap-2">
                <Icon icon={Clock} size={16} color={colors.inkMuted} />
                <Text className="font-ui500 text-label text-ink-muted">{experience.data.durationLabel}</Text>
              </View>
            ) : null}

            <OperatorNote companyName={experience.data.companyName} />

            {experience.data.description ? (
              <Text className="mt-5 font-sans text-body text-ink">{experience.data.description}</Text>
            ) : null}

            {experience.data.includesText ? (
              <DetailBlock title="Qué incluye" body={experience.data.includesText} />
            ) : null}
            {experience.data.excludesText ? (
              <DetailBlock title="Qué no incluye" body={experience.data.excludesText} />
            ) : null}

            <Gallery images={experience.data.images} />

            <AvailabilitySection
              isLoading={availability.isPending}
              slots={availability.data ?? []}
              total={availability.data?.length}
            >
              {availability.data?.slice(0, DETAIL_DATES_PREVIEW).map((slot) => (
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
          ctaLabel={noDates ? 'Sin fechas disponibles' : 'Elegir fecha'}
          disabled={noDates}
          onPress={() => router.push(`/book/experience/${id}` as Href)}
        />
      ) : null}
    </View>
  )
}
