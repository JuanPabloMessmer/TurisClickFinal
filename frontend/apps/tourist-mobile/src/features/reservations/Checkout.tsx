import type { ReservationResponse } from '@turisclick/api-client'
import { useRouter, type Href } from 'expo-router'
import { useEffect, useRef, useState } from 'react'
import { ScrollView, Text, View } from 'react-native'
import {
  useCancelReservation,
  useInvalidateReservation,
  usePayReservation,
  useReservation,
} from '@/features/reservations/api'
import { CountdownBanner, ReservationItemCard, TotalsList } from '@/features/reservations/components'
import { describeCancelFailure, describePayFailure, isReservationNotFound } from '@/features/reservations/errors'
import { priceChangeLines, projectedTotals } from '@/features/reservations/model'
import { confirmCancellation } from '@/features/reservations/ReservationDetail'
import { useCountdown } from '@/features/reservations/useCountdown'
import { IS_DEVELOPMENT } from '@/lib/env'
import { toApiError } from '@/lib/errors'
import { Button, EmptyState, ErrorState, FormError, Price, Skeleton } from '@/ui'

/**
 * Checkout de una reserva PENDING_PAYMENT con el gateway SIMULADO del backend. No hay tarjeta ni
 * pasarela: "Pagar" envía `success: true`. El backend es la autoridad sobre precio, cupo y expiración.
 */
export function Checkout({
  id,
  quotedPrice,
  quotedCurrency,
}: {
  id: string
  /** Precio que la persona vio antes de reservar, para avisar si el snapshot quedó distinto. */
  quotedPrice?: number
  quotedCurrency?: string
}) {
  const query = useReservation(id)
  const reservation = query.data
  const isPending = reservation?.status === 'PENDING_PAYMENT'
  const countdown = useCountdown(isPending ? reservation?.expiresAt : null)
  const reread = useInvalidateReservation(id)

  const pay = usePayReservation(id)
  const cancel = useCancelReservation(id)

  // Bloqueo síncrono: un segundo toque antes del re-render con `isPending` no dispara otra request.
  const busy = useRef(false)
  const [priceChange, setPriceChange] = useState<ReservationResponse | null>(null)
  const [declineReason, setDeclineReason] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [expiredByServer, setExpiredByServer] = useState(false)

  const timeUp = isPending && countdown.isTimeUp
  useEffect(() => {
    if (timeUp) reread()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [timeUp])

  if (query.isPending) {
    return (
      <View className="gap-3 px-5 pt-2" accessibilityLabel="Cargando reserva">
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-14 w-full" />
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

  if (reservation.status === 'CONFIRMED') return <PaymentSuccess reservation={reservation} />
  if (reservation.status === 'EXPIRED' || expiredByServer) return <CheckoutClosed kind="EXPIRED" id={id} />
  if (reservation.status === 'CANCELLED') return <CheckoutClosed kind="CANCELLED" id={id} />
  if (reservation.status !== 'PENDING_PAYMENT') return <CheckoutClosed kind="UNKNOWN" id={id} />

  const acting = pay.isPending || cancel.isPending
  const payDisabled = countdown.isTimeUp || cancel.isPending

  const runPay = ({ success, acceptPriceChanges }: { success: boolean; acceptPriceChanges: boolean }) => {
    if (busy.current || pay.isPending || cancel.isPending || countdown.isTimeUp) return
    busy.current = true
    setActionError(null)
    setDeclineReason(null)

    pay.mutate(
      { success, acceptPriceChanges },
      {
        onSuccess: (response) => {
          if (response.requiresPriceAcceptance) {
            setPriceChange(response)
            return
          }
          setPriceChange(null)
          // Aprobado: la caché ya quedó CONFIRMED y la pantalla pasa sola al éxito.
          if (response.paymentApproved === false) {
            setDeclineReason(response.paymentFailureReason ?? 'El pago fue rechazado.')
          }
        },
        onError: (error) => {
          const failure = describePayFailure(error)
          if (failure.kind === 'EXPIRED') {
            setExpiredByServer(true)
            return
          }
          if (failure.kind === 'STALE') setPriceChange(null)
          setActionError(failure.message)
        },
        onSettled: () => {
          busy.current = false
        },
      },
    )
  }

  const onCancel = () =>
    confirmCancellation(() => {
      if (busy.current || pay.isPending || cancel.isPending) return
      busy.current = true
      setActionError(null)
      cancel.mutate(undefined, {
        onError: (error) => setActionError(describeCancelFailure(error).message),
        onSettled: () => {
          busy.current = false
        },
      })
    })

  const items = reservation.items ?? []
  const onlyItem = items.length === 1 ? items[0] : undefined
  const priceUpdatedAtBooking =
    quotedPrice != null &&
    onlyItem != null &&
    !priceChange &&
    (onlyItem.unitPrice !== quotedPrice || (quotedCurrency ? onlyItem.currency !== quotedCurrency : false))

  return (
    <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 48, gap: 16 }} showsVerticalScrollIndicator={false}>
      <CountdownBanner label={countdown.label} isTimeUp={countdown.isTimeUp} />

      {priceUpdatedAtBooking ? (
        <View className="rounded-2xl bg-[#E8EEF2] p-4">
          <Text className="text-sm leading-5 text-ink">
            El precio se actualizó al reservar: ahora es {onlyItem.currency} {(onlyItem.unitPrice ?? 0).toFixed(2)} por persona.
          </Text>
        </View>
      ) : null}

      <View className="gap-3">
        {items.map((item) => (
          <ReservationItemCard key={item.id} item={item} reservationStatus={reservation.status} />
        ))}
      </View>

      <View className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }}>
        <TotalsList totals={reservation.totals} />
      </View>

      <View accessibilityRole="text" className="rounded-2xl border border-dashed border-[#CBD5E1] p-4">
        <Text className="text-sm font-semibold text-ink">Pago de demostración</Text>
        <Text className="mt-1 text-sm leading-5 text-[#5B7285]">
          TurisClick todavía no procesa pagos reales: no se realiza ningún cobro ni se piden datos de tarjeta.
        </Text>
      </View>

      {priceChange ? (
        <PriceChangePanel
          response={priceChange}
          disabled={payDisabled}
          paying={pay.isPending}
          onAccept={() => runPay({ success: true, acceptPriceChanges: true })}
          onBack={() => setPriceChange(null)}
        />
      ) : null}

      {declineReason ? (
        <View accessibilityRole="alert" className="rounded-2xl border border-[#FECACA] bg-[#FEF2F2] p-4">
          <Text className="text-sm font-semibold text-[#B91C1C]">El pago fue rechazado</Text>
          <Text className="mt-1 text-sm leading-5 text-[#B91C1C]">
            {declineReason} Tu reserva sigue pendiente: podés reintentar mientras quede tiempo.
          </Text>
        </View>
      ) : null}

      <FormError message={actionError} />

      {!priceChange ? (
        <View className="gap-3">
          <Button
            label={declineReason ? 'Reintentar pago' : 'Pagar'}
            loading={pay.isPending}
            disabled={payDisabled}
            onPress={() => runPay({ success: true, acceptPriceChanges: false })}
          />
          {IS_DEVELOPMENT ? (
            <Button
              label="Simular rechazo"
              variant="outline"
              disabled={payDisabled || pay.isPending}
              onPress={() => runPay({ success: false, acceptPriceChanges: false })}
            />
          ) : null}
        </View>
      ) : null}

      <Button label="Cancelar reserva" variant="outline" loading={cancel.isPending} disabled={pay.isPending} onPress={onCancel} />
      {acting ? <Text className="text-center text-xs text-[#5B7285]">Procesando…</Text> : null}
    </ScrollView>
  )
}

/**
 * El precio vigente cambió desde que se reservó. Se muestra el snapshot original contra el precio actual
 * de cada línea y los nuevos totales por moneda. Aceptar reenvía el pago con `acceptPriceChanges: true`:
 * el backend cobra el precio vigente en ese momento, y el importe final es el que confirma su respuesta.
 */
function PriceChangePanel({
  response,
  disabled,
  paying,
  onAccept,
  onBack,
}: {
  response: ReservationResponse
  disabled: boolean
  paying: boolean
  onAccept: () => void
  onBack: () => void
}) {
  const lines = priceChangeLines(response)
  const totals = projectedTotals(response)

  return (
    <View accessibilityRole="alert" className="gap-4 rounded-2xl border border-[#FDE68A] bg-[#FFFBEB] p-4">
      <View>
        <Text className="text-base font-bold text-ink">El precio cambió</Text>
        <Text className="mt-1 text-sm leading-5 text-[#5B7285]">
          El operador actualizó la tarifa desde que reservaste. No se cobró nada todavía.
        </Text>
      </View>

      {lines.map((line) => (
        <View key={line.itemId} className="gap-1">
          <Text className="text-sm font-semibold text-ink">{line.title}</Text>
          <Text className="text-sm text-[#5B7285]">
            Antes: {line.previousCurrency} {line.previousUnitPrice.toFixed(2)} por persona
          </Text>
          <Text className="text-sm font-semibold text-ink">
            Ahora: {line.currentCurrency} {line.currentUnitPrice.toFixed(2)} por persona
          </Text>
          {line.currentCurrency !== line.previousCurrency ? (
            <Text className="text-xs text-[#92400E]">La moneda también cambió.</Text>
          ) : null}
        </View>
      ))}

      <View accessibilityLabel="Nuevo total">
        <Text className="text-sm text-[#5B7285]">Nuevo total</Text>
        {totals.map((total) => (
          <Price key={total.currency} amount={total.amount} currency={total.currency} />
        ))}
      </View>

      <View className="gap-3">
        <Button label="Aceptar y pagar" loading={paying} disabled={disabled} onPress={onAccept} />
        <Button label="Volver" variant="outline" disabled={paying} onPress={onBack} />
      </View>
    </View>
  )
}

function PaymentSuccess({ reservation }: { reservation: ReservationResponse }) {
  const router = useRouter()

  const goTo = (href: Href) => {
    if (router.canDismiss()) router.dismissAll()
    router.navigate(href)
  }

  return (
    <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 48, gap: 16 }} showsVerticalScrollIndicator={false}>
      <View className="items-center rounded-2xl bg-[#DCFCE7] p-6">
        <Text className="text-4xl">🎉</Text>
        <Text className="mt-2 text-xl font-bold text-[#166534]">¡Reserva confirmada!</Text>
        <Text className="mt-1 text-center text-sm leading-5 text-[#166534]">
          Tu lugar quedó asegurado. Podés ver los detalles cuando quieras en Mis viajes.
        </Text>
      </View>

      <View className="gap-3">
        {(reservation.items ?? []).map((item) => (
          <ReservationItemCard key={item.id} item={item} reservationStatus={reservation.status} />
        ))}
      </View>

      <View className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }}>
        <TotalsList totals={reservation.totals} label="Total confirmado" />
      </View>

      <Button label="Ver mis viajes" onPress={() => goTo('/trips')} />
      <Button label="Seguir explorando" variant="outline" onPress={() => goTo('/explore')} />
    </ScrollView>
  )
}

function CheckoutClosed({ kind, id }: { kind: 'EXPIRED' | 'CANCELLED' | 'UNKNOWN'; id: string }) {
  const router = useRouter()

  const copy = {
    EXPIRED: {
      title: 'Se venció el tiempo para pagar',
      message: 'La reserva no se pagó a tiempo y el cupo retenido se liberó. Podés volver a elegir una fecha.',
    },
    CANCELLED: { title: 'Cancelaste esta reserva', message: 'El cupo se liberó. Podés volver a reservar cuando quieras.' },
    UNKNOWN: { title: 'Esta reserva no se puede pagar', message: 'Revisá su estado en el detalle de la reserva.' },
  }[kind]

  return (
    <View className="gap-4 px-5 pt-6">
      <EmptyState title={copy.title} message={copy.message} />
      <Button label="Ver la reserva" variant="outline" onPress={() => router.replace(`/reservation/${id}` as Href)} />
      <Button label="Seguir explorando" onPress={() => router.navigate('/explore')} />
    </View>
  )
}
