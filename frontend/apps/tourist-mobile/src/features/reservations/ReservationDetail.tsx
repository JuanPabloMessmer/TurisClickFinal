import { useRouter, type Href } from 'expo-router'
import { useEffect, useRef, useState } from 'react'
import { Alert, ScrollView, Text, View } from 'react-native'
import { useCancelReservation, useInvalidateReservation, useReservation } from '@/features/reservations/api'
import {
  CountdownBanner,
  ReservationItemCard,
  StatusBadge,
  TotalsList,
} from '@/features/reservations/components'
import { describeCancelFailure, isReservationNotFound } from '@/features/reservations/errors'
import { canCancel, canPay, displayStatus } from '@/features/reservations/model'
import { useCountdown } from '@/features/reservations/useCountdown'
import { toApiError } from '@/lib/errors'
import { Button, EmptyState, ErrorState, FormError, Skeleton } from '@/ui'

/** Pedir confirmación antes de cancelar: libera el cupo y no se puede deshacer. */
export function confirmCancellation(onConfirm: () => void) {
  Alert.alert(
    '¿Cancelar la reserva?',
    'Se libera el cupo retenido y no se puede deshacer.',
    [
      { text: 'Volver', style: 'cancel' },
      { text: 'Cancelar reserva', style: 'destructive', onPress: onConfirm },
    ],
  )
}

/** Detalle de una reserva propia, en cualquier estado. */
export function ReservationDetail({ id }: { id: string }) {
  const router = useRouter()
  const query = useReservation(id)
  const reservation = query.data
  const isPending = reservation?.status === 'PENDING_PAYMENT'
  const countdown = useCountdown(isPending ? reservation?.expiresAt : null)
  const reread = useInvalidateReservation(id)

  const cancel = useCancelReservation(id)
  const cancelling = useRef(false)
  const [cancelError, setCancelError] = useState<string | null>(null)

  // Al llegar a 00:00 se re-lee una vez; si el backend todavía no la expiró, useReservation hace el
  // polling limitado. El frontend nunca cambia el estado por su cuenta.
  const timeUp = isPending && countdown.isTimeUp
  useEffect(() => {
    if (timeUp) reread()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [timeUp])

  if (query.isPending) {
    return (
      <View className="gap-3 px-5 pt-2" accessibilityLabel="Cargando reserva">
        <Skeleton className="h-6 w-32 rounded-full" />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-16 w-full" />
      </View>
    )
  }

  if (query.isError || !reservation) {
    return isReservationNotFound(query.error) ? (
      <EmptyState title="No encontramos esta reserva" message="Puede que el enlace sea incorrecto o que no sea tuya." />
    ) : (
      <ErrorState message={toApiError(query.error).message} onRetry={query.refetch} />
    )
  }

  const status = displayStatus(reservation, countdown.now)

  const onCancel = () =>
    confirmCancellation(() => {
      if (cancelling.current || cancel.isPending) return
      cancelling.current = true
      setCancelError(null)
      cancel.mutate(undefined, {
        onError: (error) => setCancelError(describeCancelFailure(error).message),
        onSettled: () => {
          cancelling.current = false
        },
      })
    })

  return (
    <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 40, gap: 16 }} showsVerticalScrollIndicator={false}>
      <StatusBadge status={status} />

      {isPending ? <CountdownBanner label={countdown.label} isTimeUp={countdown.isTimeUp} /> : null}

      {status.kind === 'EXPIRED' ? (
        <Text className="text-sm leading-5 text-[#5B7285]">
          No se completó el pago a tiempo y el cupo se liberó. Podés volver a reservar desde el catálogo.
        </Text>
      ) : null}

      <View className="gap-3">
        {(reservation.items ?? []).map((item) => (
          <ReservationItemCard key={item.id} item={item} reservationStatus={reservation.status} />
        ))}
      </View>

      <View className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }}>
        <TotalsList totals={reservation.totals} />
      </View>

      <FormError message={cancelError} />

      {canPay(reservation, countdown.now) ? (
        <Button label="Pagar" onPress={() => router.push(`/checkout/${id}` as Href)} disabled={cancel.isPending} />
      ) : null}

      {canCancel(reservation) ? (
        <Button label="Cancelar reserva" variant="outline" loading={cancel.isPending} onPress={onCancel} />
      ) : null}
    </ScrollView>
  )
}
