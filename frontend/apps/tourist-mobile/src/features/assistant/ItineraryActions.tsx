import type { BookItineraryResponse, ItineraryResponse } from '@turisclick/api-client'
import { useRouter } from 'expo-router'
import { useRef, useState } from 'react'
import { Text, View } from 'react-native'
import { useBookItinerary, useSaveItinerary } from '@/features/assistant/api'
import { canBook, canSave, describeBookingFailure } from '@/features/assistant/model'
import { toApiError } from '@/lib/errors'
import { Button, FormError } from '@/ui'

/**
 * Guardar y reservar un itinerario. Reservar es atómico en el backend (todo o nada) y deja la reserva en
 * PENDING_PAYMENT: el pago es el checkout de siempre. Si algún precio cambió, el backend no reserva nada
 * hasta que el turista acepte los precios nuevos.
 */
export function ItineraryActions({ itinerary }: { itinerary: ItineraryResponse }) {
  const router = useRouter()
  const save = useSaveItinerary()
  const book = useBookItinerary()
  const inFlight = useRef(false)
  const [priceChanges, setPriceChanges] = useState<BookItineraryResponse['changes'] | null>(null)
  const [failure, setFailure] = useState<{ message: string; checkTrips: boolean } | null>(null)

  const id = itinerary.id ?? ''
  const bookable = canBook(itinerary)

  const onBook = (acceptPriceChanges: boolean) => {
    if (inFlight.current || book.isPending) return
    inFlight.current = true
    setFailure(null)
    book.mutate(
      { itineraryId: id, acceptPriceChanges },
      {
        onSuccess: (result) => {
          if (result.requiresPriceAcceptance) {
            setPriceChanges(result.changes ?? [])
            return
          }
          setPriceChanges(null)
          if (result.reservation?.id) router.push({ pathname: '/checkout/[id]', params: { id: result.reservation.id } })
        },
        onError: (error) => setFailure(describeBookingFailure(error)),
        onSettled: () => {
          inFlight.current = false
        },
      },
    )
  }

  if (itinerary.status === 'BOOKED') {
    return (
      <View className="gap-2">
        <Text className="text-center text-sm text-[#5B7285]">Este itinerario ya está reservado.</Text>
        <Button label="Ver mis viajes" variant="outline" onPress={() => router.push('/trips')} />
      </View>
    )
  }

  return (
    <View className="gap-3">
      {!itinerary.isStillBookable ? (
        <Text className="text-sm leading-5 text-[#991B1B]">
          Algún componente ya no se puede reservar tal como está. Pedile al asistente que lo cambie.
        </Text>
      ) : null}

      {priceChanges ? (
        <View className="gap-2 rounded-2xl border border-[#FDE68A] bg-[#FFFBEB] p-4">
          <Text className="text-base font-semibold text-ink">Cambiaron algunos precios</Text>
          {priceChanges.map((change) => (
            <Text key={change.itineraryItemId} className="text-sm text-ink">
              {change.productTitle}: {change.previousCurrency} {change.previousUnitPrice?.toFixed(2)} → {change.currentCurrency}{' '}
              {change.currentUnitPrice?.toFixed(2)}
            </Text>
          ))}
          <Button label="Aceptar precios y reservar" loading={book.isPending} onPress={() => onBook(true)} />
        </View>
      ) : null}

      {failure ? <FormError message={failure.message} /> : null}
      {failure?.checkTrips ? <Button label="Ir a Mis viajes" variant="outline" onPress={() => router.push('/trips')} /> : null}
      {save.isError ? <FormError message={toApiError(save.error).message} /> : null}

      {!priceChanges ? (
        <Button label="Reservar itinerario" disabled={!bookable} loading={book.isPending} onPress={() => onBook(false)} />
      ) : null}
      {canSave(itinerary) ? (
        <Button label="Guardar para después" variant="outline" loading={save.isPending} onPress={() => save.mutate(id)} />
      ) : itinerary.status === 'SAVED' ? (
        <Text className="text-center text-sm font-medium text-secondary">✓ Guardado en tus itinerarios</Text>
      ) : null}
      <Text className="text-center text-xs text-[#5B7285]">Guardar no retiene cupos. Al reservar se toman todos juntos o ninguno.</Text>
    </View>
  )
}
