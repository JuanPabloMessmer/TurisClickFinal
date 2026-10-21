import type { flightsApi } from '@turisclick/api-client'
import { todayIso } from '@turisclick/utils'
import { useQueryClient } from '@tanstack/react-query'
import { useRouter } from 'expo-router'
import { useRef, useState } from 'react'
import { ScrollView, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useSession } from '@/auth/session'
import { useFlightSearchResults } from '@/features/flights/api'
import {
  buildFlightReservationRequest,
  emptyPassenger,
  findSelectedOption,
  hasErrors,
  passengerMixProblem,
  passengerSlots,
  samplePassengers,
  seatedCount,
  validatePassenger,
  type PassengerDraft,
  type PassengerErrors,
  type TravelDates,
} from '@/features/flights/booking/model'
import { PassengerForm } from '@/features/flights/booking/PassengerForm'
import { DemoTag } from '@/features/flights/booking/ProviderFlightItemCard'
import { FlightOfferCard } from '@/features/flights/search/results'
import { useCreateReservation } from '@/features/reservations/api'
import { describeFlightCreateFailure, type FlightCreateFailureKind } from '@/features/reservations/errors'
import { toApiError } from '@/lib/errors'
import { Button, EmptyState, ErrorState, FormError, Skeleton } from '@/ui'

/**
 * Reservar un vuelo de operador: revisar el vuelo elegido, cargar los pasajeros y crear la reserva. El pago
 * es el checkout de siempre (simulado).
 *
 * La opción se vuelve a encontrar en la búsqueda (misma clave de caché que los resultados) por sus salidas,
 * que viajan en la URL. Por eso iniciar sesión en el medio no pierde nada: el modal de login se cierra y esta
 * misma pantalla, con los mismos parámetros, muestra ahora el formulario.
 *
 * Mirar el vuelo no exige sesión; cargar pasajeros y reservar sí.
 */
export function FlightBookingScreen({
  request,
  departureIds,
  today = todayIso(),
}: {
  request: flightsApi.SearchFlightsRequest | null
  departureIds: string[] | null
  /** Sólo tests: fija "hoy" para que las reglas de edad sean determinísticas. */
  today?: string
}) {
  const results = useFlightSearchResults(request && departureIds ? request : null)

  if (!request || !departureIds) {
    return (
      <View className="px-5 pt-6">
        <EmptyState title="No encontramos ese vuelo" message="Volvé a buscar y elegí un vuelo de la lista." />
      </View>
    )
  }

  if (results.isPending) {
    return (
      <View className="gap-3 px-5 pt-2" accessibilityLabel="Cargando vuelo">
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-24 w-full" />
      </View>
    )
  }

  if (results.isError) {
    return (
      <View className="px-5 pt-6">
        <ErrorState message={toApiError(results.error).message} onRetry={() => void results.refetch()} />
      </View>
    )
  }

  const option = findSelectedOption(results.data?.options, departureIds)
  if (!option) return <FlightGone />

  return <BookingForm request={request} option={option} departureIds={departureIds} today={today} />
}

function FlightGone() {
  const router = useRouter()
  return (
    <View className="gap-4 px-5 pt-6">
      <EmptyState
        title="Este vuelo ya no está disponible"
        message="Puede que se hayan agotado los asientos o que el operador haya dejado de venderlo. Elegí otro de la lista."
      />
      <Button label="Volver a los resultados" variant="outline" onPress={() => (router.canGoBack() ? router.back() : router.replace('/flights'))} />
    </View>
  )
}

function BookingForm({
  request,
  option,
  departureIds,
  today,
}: {
  request: flightsApi.SearchFlightsRequest
  option: flightsApi.FlightOptionResponse
  departureIds: string[]
  today: string
}) {
  const router = useRouter()
  const insets = useSafeAreaInsets()
  const queryClient = useQueryClient()
  const { status: sessionStatus, isAuthenticated } = useSession()
  const create = useCreateReservation()

  const slots = passengerSlots(request)
  const mixProblem = passengerMixProblem(slots)
  const seated = seatedCount(slots)
  const roundTrip = departureIds.length === 2
  const totalPassengers = slots.length

  // Los días reales del vuelo (hora local), no los de la búsqueda: son los que deciden la edad de cada uno.
  const slices = option.slices ?? []
  const dates: TravelDates = {
    departure: slices[0]?.segments?.[0]?.departingAt?.slice(0, 10) ?? request.departureDate ?? today,
    return: roundTrip ? (slices[1]?.segments?.[0]?.departingAt?.slice(0, 10) ?? request.returnDate ?? null) : null,
    today,
  }

  const [passengers, setPassengers] = useState<PassengerDraft[]>(() => slots.map(emptyPassenger))
  const [errors, setErrors] = useState<PassengerErrors[]>([])
  const [failure, setFailure] = useState<{ kind: FlightCreateFailureKind; message: string } | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  // Bloqueo síncrono: un segundo toque antes del re-render no crea otra reserva (el POST no es idempotente).
  const inFlight = useRef(false)

  const sessionUnknown = sessionStatus === 'idle' || sessionStatus === 'loading'
  const notAvailable = failure?.kind === 'NOT_AVAILABLE'
  const price = option.price

  const onReserve = () => {
    if (inFlight.current || create.isPending || !isAuthenticated || mixProblem) return

    const nextErrors = slots.map((slot, index) => validatePassenger(passengers[index], slot, dates))
    setErrors(nextErrors)
    if (nextErrors.some(hasErrors)) {
      setFormError('Revisá los datos de los pasajeros antes de reservar.')
      return
    }

    inFlight.current = true
    setFormError(null)
    setFailure(null)

    create.mutate(buildFlightReservationRequest(departureIds, slots, passengers), {
      onSuccess: (reservation) => {
        // Se tomaron asientos: la búsqueda en caché ya no dice cuántos quedan.
        void queryClient.invalidateQueries({ queryKey: ['flights', 'search'] })
        // replace: volver atrás desde el checkout no debe traer este formulario, que invitaría a reservar dos veces.
        router.replace({
          pathname: '/checkout/[id]',
          params: {
            id: reservation.id ?? '',
            // Precio total que vio la persona: el checkout avisa si el backend congeló otro.
            quotedPrice: price?.amount != null ? String(price.amount) : '',
            quotedCurrency: price?.currency ?? '',
          },
        })
      },
      onError: (error) => setFailure(describeFlightCreateFailure(error, seated)),
      onSettled: () => {
        inFlight.current = false
      },
    })
  }

  return (
    <View className="flex-1">
      <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 220, gap: 16 }} showsVerticalScrollIndicator={false}>
        <Text className="font-ui700 text-title text-ink">{roundTrip ? 'Tu vuelo de ida y vuelta' : 'Tu vuelo de ida'}</Text>

        <FlightOfferCard option={option} passengers={totalPassengers} demo />

        {option.providerFlight?.fareRules ? (
          <View className="rounded-md bg-surface p-4">
            <Text className="font-ui600 text-label text-ink">Condiciones de la tarifa</Text>
            <Text className="mt-1 font-sans text-caption text-ink-muted">{option.providerFlight.fareRules}</Text>
          </View>
        ) : null}

        {sessionUnknown ? (
          <Skeleton className="h-32 w-full" />
        ) : !isAuthenticated ? (
          <View testID="login-required" className="gap-3 rounded-lg border border-border bg-surface p-5">
            <Text className="font-display text-title text-ink">Iniciá sesión para reservar</Text>
            <Text className="font-sans text-body text-ink-muted">
              Tu vuelo queda elegido: después de entrar volvés acá para cargar los pasajeros.
            </Text>
            <Button label="Iniciar sesión" onPress={() => router.push('/(auth)/login')} />
            <Button label="Crear cuenta" variant="outline" onPress={() => router.push('/(auth)/register')} />
          </View>
        ) : (
          <>
            <View className="gap-2">
              <View className="flex-row items-center justify-between gap-2">
                <Text className="font-ui700 text-heading text-ink">Pasajeros</Text>
                <DemoTag />
              </View>
              <Text className="font-sans text-caption text-ink-muted">
                Es una demostración: usá nombres y fechas de nacimiento ficticios. No te pedimos documento de identidad.
              </Text>
              <Button
                label="Completar con datos de ejemplo"
                variant="outline"
                onPress={() => {
                  setPassengers(samplePassengers(slots, dates))
                  setErrors([])
                  setFormError(null)
                }}
              />
            </View>

            {mixProblem ? <FormError message={mixProblem} /> : null}

            {slots.map((slot, index) => (
              <PassengerForm
                key={index}
                index={index}
                slot={slot}
                passenger={passengers[index]}
                errors={errors[index] ?? {}}
                onChange={(updated) => setPassengers((current) => current.map((p, i) => (i === index ? updated : p)))}
              />
            ))}
          </>
        )}

        <FormError message={formError} />

        {failure && notAvailable ? (
          <View testID="feature-not-available" accessibilityRole="alert" className="gap-3 rounded-md border border-warning-fg/30 bg-warning-bg p-4">
            <Text className="font-ui700 text-label text-warning-fg">Reserva de vuelos todavía no disponible</Text>
            <Text className="font-sans text-caption text-warning-fg">{failure.message}</Text>
          </View>
        ) : failure ? (
          <View className="gap-3">
            <FormError message={failure.message} />
            {failure.kind === 'NETWORK' ? (
              <Button label="Ir a Mis viajes" variant="outline" onPress={() => router.push('/trips')} />
            ) : failure.kind === 'NO_CAPACITY' || failure.kind === 'SLOT_UNAVAILABLE' || failure.kind === 'PRODUCT_UNAVAILABLE' ? (
              <Button label="Volver a los resultados" variant="outline" onPress={() => router.back()} />
            ) : null}
          </View>
        ) : null}
      </ScrollView>

      {isAuthenticated ? (
        <View
          className="absolute inset-x-0 bottom-0 border-t border-border bg-surface px-5 pt-4"
          style={{ paddingBottom: insets.bottom + 16 }}
        >
          <Text className="mb-3 font-sans text-caption text-ink-muted">
            Total estimado para {totalPassengers} {totalPassengers === 1 ? 'pasajero' : 'pasajeros'}: el precio se congela al
            reservar y los asientos quedan retenidos mientras pagás.
          </Text>
          <Button
            label={notAvailable ? 'Todavía no disponible' : 'Reservar'}
            loading={create.isPending}
            disabled={notAvailable || Boolean(mixProblem)}
            onPress={onReserve}
          />
        </View>
      ) : null}
    </View>
  )
}
