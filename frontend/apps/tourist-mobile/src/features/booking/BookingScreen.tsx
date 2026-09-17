import { parseIsoDate, type IsoDate } from '@turisclick/utils'
import { useRouter, type Href } from 'expo-router'
import { useMemo, useRef, useState } from 'react'
import { Pressable, ScrollView, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useSession } from '@/auth/session'
import { AvailabilityCalendar } from '@/features/booking/AvailabilityCalendar'
import { slotsByDate } from '@/features/booking/calendarModel'
import { estimatedTotal, type BookableSlot } from '@/features/booking/selection'
import { useBookingSelection } from '@/features/booking/useBookingSelection'
import { slotsLabel } from '@/features/catalog/detail'
import { useCreateReservation } from '@/features/reservations/api'
import { ScreenHeader } from '@/features/reservations/components'
import { describeCreateFailure, type CreateReservationFailureKind } from '@/features/reservations/errors'
import { toApiError } from '@/lib/errors'
import { Button, EmptyState, ErrorState, FormError, Price, Screen, Skeleton } from '@/ui'

interface QueryState {
  isPending: boolean
  isError: boolean
  error: unknown
  refetch: () => unknown
}

export interface BookingProduct {
  title?: string | null
  companyName?: string | null
  price?: number | null
  currency?: string | null
}

/**
 * Selección de fecha y viajeros antes de reservar. La pantalla es pública: cualquiera puede elegir. Crear
 * la reserva exige sesión — el invitado pasa por login y vuelve acá con su selección; la reserva NO se
 * crea sola al volver: tiene que tocar "Continuar" de nuevo, porque crearla retiene cupo.
 */
export function BookingScreen({
  productType,
  productHref,
  product,
  productQuery,
  slots,
  availabilityQuery,
  today,
}: {
  productType: 'EXPERIENCE' | 'PACKAGE'
  productHref: Href
  product?: BookingProduct
  productQuery: QueryState
  slots: BookableSlot[]
  availabilityQuery: QueryState
  /** Solo tests: fija "hoy" para que el calendario sea determinístico. */
  today?: IsoDate
}) {
  const router = useRouter()
  const insets = useSafeAreaInsets()
  const { status: sessionStatus, isAuthenticated } = useSession()
  const selection = useBookingSelection(slots, !availabilityQuery.isPending)
  const create = useCreateReservation()
  const byDate = useMemo(() => slotsByDate(slots), [slots])
  const [selectedDate, setSelectedDate] = useState<IsoDate | null>(null)
  const daySlots = selectedDate ? byDate.get(selectedDate) ?? [] : []

  const onSelectDate = (date: IsoDate) => {
    setFailure(null)
    setSelectedDate(date)
    const options = byDate.get(date) ?? []
    // Un solo horario (o una salida de paquete): se elige directo, sin un paso extra.
    if (options.length === 1) selection.select(options[0].id)
    else selection.clearSelection()
  }

  // Un toque que llega antes de que React re-renderice con `isPending` no debe disparar una segunda
  // reserva: sin idempotencia en el backend, cada POST exitoso retiene cupo.
  const inFlight = useRef(false)
  const [failure, setFailure] = useState<{ kind: CreateReservationFailureKind; message: string } | null>(null)

  const isPackage = productType === 'PACKAGE'
  const sessionUnknown = sessionStatus === 'idle' || sessionStatus === 'loading'
  const estimate = estimatedTotal(product?.price, selection.travelers)

  const onContinue = () => {
    const slot = selection.selected
    if (!slot || sessionUnknown) return

    if (!isAuthenticated) {
      router.push('/(auth)/login')
      return
    }

    if (inFlight.current || create.isPending) return
    inFlight.current = true
    setFailure(null)

    const travelers = selection.travelers
    create.mutate(
      isPackage
        ? { packageAvailabilityId: slot.id, travelers }
        : { experienceAvailabilityId: slot.id, travelers },
      {
        onSuccess: (reservation) => {
          // replace: volver atrás desde el checkout no debe traer de nuevo este formulario, que invitaría
          // a crear una segunda reserva.
          router.replace({
            pathname: '/checkout/[id]',
            params: {
              id: reservation.id ?? '',
              quotedPrice: product?.price != null ? String(product.price) : '',
              quotedCurrency: product?.currency ?? '',
            },
          })
        },
        onError: (error) => {
          const described = describeCreateFailure(error, travelers)
          if (described.kind === 'SLOT_UNAVAILABLE') selection.clearSelection()
          setFailure(described)
        },
        onSettled: () => {
          inFlight.current = false
        },
      },
    )
  }

  const title = isPackage ? 'Elegir salida' : 'Elegir fecha'

  if (productQuery.isError || failure?.kind === 'PRODUCT_UNAVAILABLE') {
    return (
      <Screen edges={['top', 'bottom']}>
        <ScreenHeader title={title} fallback={productHref} />
        {failure?.kind === 'PRODUCT_UNAVAILABLE' || toApiError(productQuery.error).status === 404 ? (
          <EmptyState
            title="Este producto ya no está disponible"
            message="Puede que el operador lo haya retirado. Te invitamos a seguir explorando."
          />
        ) : (
          <ErrorState message={toApiError(productQuery.error).message} onRetry={productQuery.refetch} />
        )}
      </Screen>
    )
  }

  return (
    <Screen edges={['top']}>
      <ScreenHeader title={title} fallback={productHref} />

      <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 200 }} showsVerticalScrollIndicator={false}>
        {productQuery.isPending ? (
          <Skeleton className="h-6 w-3/4" />
        ) : (
          <View>
            <Text className="text-xl font-bold text-ink">{product?.title}</Text>
            {product?.companyName ? (
              <Text className="mt-1 text-sm text-[#5B7285]">Operado por {product.companyName}</Text>
            ) : null}
          </View>
        )}

        <Text className="mb-3 mt-8 text-lg font-bold text-ink">{isPackage ? 'Elegí la salida' : 'Elegí el día'}</Text>
        {availabilityQuery.isPending ? (
          <Skeleton className="h-80 w-full rounded-2xl" />
        ) : availabilityQuery.isError ? (
          <ErrorState message={toApiError(availabilityQuery.error).message} onRetry={availabilityQuery.refetch} />
        ) : byDate.size === 0 ? (
          <Text className="text-sm text-[#5B7285]">No hay fechas con cupo por el momento. Consultá más adelante.</Text>
        ) : (
          <AvailabilityCalendar slots={slots} selectedDate={selectedDate} onSelectDate={onSelectDate} today={today} />
        )}

        {selectedDate && daySlots.length > 0 ? (
          <View className="mt-6">
            <Text className="mb-3 text-base font-semibold text-ink">{capitalize(longDate.format(parseIsoDate(selectedDate)))}</Text>
            {daySlots.length > 1 ? (
              <>
                <Text className="mb-2 text-sm text-[#5B7285]">Elegí un horario</Text>
                <View className="gap-2" accessibilityRole="radiogroup">
                  {daySlots.map((slot) => (
                    <SelectableSlot
                      key={slot.id}
                      slot={slot}
                      selected={slot.id === selection.selectedId}
                      onPress={() => {
                        setFailure(null)
                        selection.select(slot.id)
                      }}
                    />
                  ))}
                </View>
              </>
            ) : (
              <SelectableSlot slot={daySlots[0]} selected={daySlots[0].id === selection.selectedId} onPress={() => selection.select(daySlots[0].id)} />
            )}
          </View>
        ) : null}

        <Text className="mb-3 mt-8 text-lg font-bold text-ink">Viajeros</Text>
        <TravelersStepper
          value={selection.travelers}
          canDecrement={selection.canDecrement}
          canIncrement={selection.canIncrement}
          onDecrement={selection.decrement}
          onIncrement={selection.increment}
        />
        {selection.selected ? (
          <Text className="mt-2 text-sm text-[#5B7285]">
            Máximo {selection.maxTravelers} para {isPackage ? 'esta salida' : 'esta fecha'}.
          </Text>
        ) : null}

        {failure ? (
          <View className="mt-6 gap-3">
            <FormError message={failure.message} />
            {failure.kind === 'NETWORK' ? (
              <Button label="Ir a Mis viajes" variant="outline" onPress={() => router.push('/trips')} />
            ) : null}
          </View>
        ) : null}
      </ScrollView>

      <View
        className="absolute inset-x-0 bottom-0 border-t border-[#E2E8F0] bg-surface px-5 pt-4"
        style={{ paddingBottom: insets.bottom + 16 }}
      >
        <View className="mb-3 flex-row items-baseline justify-between">
          <View className="flex-1 pr-3">
            <Text className="text-sm text-[#5B7285]">Estimado para {selection.travelers} {selection.travelers === 1 ? 'viajero' : 'viajeros'}</Text>
            <Text className="text-xs text-[#5B7285]">Estimado: se confirma al reservar</Text>
          </View>
          <Price amount={estimate} currency={product?.currency} size="lg" />
        </View>
        <Button
          label="Continuar"
          loading={create.isPending}
          disabled={!selection.selected || sessionUnknown}
          onPress={onContinue}
        />
        {!isAuthenticated && !sessionUnknown ? (
          <Text className="mt-2 text-center text-xs text-[#5B7285]">Vas a iniciar sesión antes de reservar.</Text>
        ) : null}
      </View>
    </Screen>
  )
}

const longDate = new Intl.DateTimeFormat('es-BO', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
const capitalize = (text: string) => `${text.charAt(0).toUpperCase()}${text.slice(1)}`

function SelectableSlot({ slot, selected, onPress }: { slot: BookableSlot; selected: boolean; onPress: () => void }) {
  const when = slot.time ? `Horario ${slot.time.slice(0, 5)}` : 'Día completo'

  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityState={{ selected, checked: selected }}
      accessibilityLabel={`${when}, ${slotsLabel(slot.availableSlots)}`}
      onPress={onPress}
      className={`min-h-[56px] flex-row items-center justify-between rounded-xl border px-4 py-3 active:opacity-80 ${
        selected ? 'border-primary bg-primary/10' : 'border-[#E2E8F0] bg-surface'
      }`}
    >
      <View className="flex-row items-center gap-3">
        <View className={`h-5 w-5 items-center justify-center rounded-full border-2 ${selected ? 'border-primary' : 'border-[#CBD5E1]'}`}>
          {selected ? <View className="h-2.5 w-2.5 rounded-full bg-primary" /> : null}
        </View>
        <Text className="text-base text-ink">{when}</Text>
      </View>
      <Text className="text-sm text-[#5B7285]">{slotsLabel(slot.availableSlots)}</Text>
    </Pressable>
  )
}

function TravelersStepper({
  value,
  canDecrement,
  canIncrement,
  onDecrement,
  onIncrement,
}: {
  value: number
  canDecrement: boolean
  canIncrement: boolean
  onDecrement: () => void
  onIncrement: () => void
}) {
  return (
    <View className="flex-row items-center gap-4">
      <StepperButton label="−" accessibilityLabel="Quitar un viajero" disabled={!canDecrement} onPress={onDecrement} />
      <Text accessibilityLabel={`${value} viajeros`} className="min-w-[40px] text-center text-2xl font-bold text-ink">
        {value}
      </Text>
      <StepperButton label="+" accessibilityLabel="Agregar un viajero" disabled={!canIncrement} onPress={onIncrement} />
    </View>
  )
}

function StepperButton({
  label,
  accessibilityLabel,
  disabled,
  onPress,
}: {
  label: string
  accessibilityLabel: string
  disabled: boolean
  onPress: () => void
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      className={`h-12 w-12 items-center justify-center rounded-full border border-primary ${disabled ? 'opacity-30' : 'active:opacity-70'}`}
    >
      <Text className="text-2xl text-primary">{label}</Text>
    </Pressable>
  )
}
