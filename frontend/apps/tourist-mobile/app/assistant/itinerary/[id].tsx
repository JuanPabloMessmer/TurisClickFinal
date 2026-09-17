import { Stack, useLocalSearchParams, useRouter } from 'expo-router'
import { RefreshControl, ScrollView, View } from 'react-native'
import { RequireTourist } from '@/auth/RequireTourist'
import { useItinerary } from '@/features/assistant/api'
import { ItineraryView } from '@/features/assistant/components'
import { ItineraryActions } from '@/features/assistant/ItineraryActions'
import { ScreenHeader } from '@/features/reservations/components'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { Button, ErrorState, Screen, Skeleton } from '@/ui'

/**
 * Retomar un itinerario guardado. El backend lo revalida al leerlo: precios que cambiaron, fechas sin cupo o
 * productos despublicados se muestran como avisos, nunca se ocultan.
 */
export default function SavedItineraryScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()

  return (
    <Screen edges={['top']}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Itinerario" fallback="/assistant" />
      <RequireTourist title="Iniciá sesión para ver tus itinerarios">
        <SavedItinerary id={id} />
      </RequireTourist>
    </Screen>
  )
}

function SavedItinerary({ id }: { id: string }) {
  const router = useRouter()
  const itinerary = useItinerary(id)

  if (itinerary.isPending) {
    return (
      <View className="gap-3 p-5">
        <Skeleton className="h-28 w-full" />
        <Skeleton className="h-40 w-full" />
      </View>
    )
  }

  if (itinerary.isError) return <ErrorState message={toApiError(itinerary.error).message} onRetry={itinerary.refetch} />

  return (
    <ScrollView
      contentContainerStyle={{ padding: 16, paddingBottom: 40 }}
      refreshControl={<RefreshControl refreshing={itinerary.isRefetching} onRefresh={() => void itinerary.refetch()} tintColor={colors.primary} />}
    >
      <ItineraryView itinerary={itinerary.data}>
        <ItineraryActions itinerary={itinerary.data} />
      </ItineraryView>
      {itinerary.data.aiConversationId ? (
        <View className="mt-4">
          <Button
            label="Seguir ajustándolo con el asistente"
            variant="outline"
            onPress={() => router.push({ pathname: '/assistant/[id]', params: { id: itinerary.data.aiConversationId ?? '' } })}
          />
        </View>
      ) : null}
    </ScrollView>
  )
}
