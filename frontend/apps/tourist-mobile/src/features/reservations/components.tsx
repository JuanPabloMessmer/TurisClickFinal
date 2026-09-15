import type { ReservationItemResponse, ReservationResponse, ReservationTotalResponse } from '@turisclick/api-client'
import { formatDate } from '@turisclick/utils'
import { useRouter, type Href } from 'expo-router'
import { Pressable, Text, View } from 'react-native'
import {
  displayStatus,
  isCancelledByOperator,
  itemStatusLabel,
  itemTitle,
  reservationTitle,
  type ReservationDisplayStatus,
  type StatusTone,
} from '@/features/reservations/model'
import { Button, Price, Skeleton } from '@/ui'

/** Piezas visuales de reservas: badges, totales por moneda, ítems y tarjetas de "Mis viajes". */

const TONE_STYLES: Record<StatusTone, { container: string; text: string }> = {
  warning: { container: 'bg-[#FEF3C7]', text: 'text-[#92400E]' },
  success: { container: 'bg-[#DCFCE7]', text: 'text-[#166534]' },
  danger: { container: 'bg-[#FEE2E2]', text: 'text-[#991B1B]' },
  neutral: { container: 'bg-[#E8EEF2]', text: 'text-[#5B7285]' },
}

export function StatusBadge({ status }: { status: ReservationDisplayStatus }) {
  const tone = TONE_STYLES[status.tone]
  return (
    <View className={`self-start rounded-full px-3 py-1 ${tone.container}`}>
      <Text className={`text-xs font-semibold ${tone.text}`}>{status.label}</Text>
    </View>
  )
}

/**
 * Totales tal como los calcula el backend: una fila por moneda. Nunca se suman monedas distintas — no hay
 * tipo de cambio en TurisClick.
 */
export function TotalsList({
  totals,
  label = 'Total',
}: {
  totals?: ReservationTotalResponse[] | null
  label?: string
}) {
  const rows = (totals ?? []).filter((total) => Boolean(total.currency))
  if (rows.length === 0) return null

  return (
    <View accessibilityLabel={label}>
      <Text className="text-sm text-[#5B7285]">{label}</Text>
      <View className="mt-1 gap-0.5">
        {rows.map((total) => (
          <Price key={total.currency} amount={total.amount} currency={total.currency} />
        ))}
      </View>
    </View>
  )
}

export function formatItemWhen(item: Pick<ReservationItemResponse, 'date' | 'startTime'>): string | null {
  if (!item.date) return null
  const time = item.startTime ? ` · ${item.startTime.slice(0, 5)}` : ''
  return `${formatDate(item.date)}${time}`
}

export function travelersLabel(travelers?: number) {
  const count = travelers ?? 0
  return `${count} ${count === 1 ? 'viajero' : 'viajeros'}`
}

/** Una línea de la reserva con su snapshot de precio y, si el operador la canceló, el motivo. */
export function ReservationItemCard({
  item,
  reservationStatus,
}: {
  item: ReservationItemResponse
  reservationStatus?: string | null
}) {
  const statusLabel = itemStatusLabel(item, reservationStatus)
  const cancelledByOperator = isCancelledByOperator(item, reservationStatus)
  const when = formatItemWhen(item)

  return (
    <View className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }}>
      <Text className="text-xs font-medium uppercase tracking-wide text-secondary">
        {item.productType === 'PACKAGE' ? 'Paquete' : 'Experiencia'}
      </Text>
      <Text className={`mt-1 text-base font-semibold ${cancelledByOperator ? 'text-[#5B7285] line-through' : 'text-ink'}`}>
        {itemTitle(item)}
      </Text>
      {item.companyName ? <Text className="mt-0.5 text-sm text-[#5B7285]">{item.companyName}</Text> : null}
      {when ? <Text className="mt-2 text-sm text-ink">{when}</Text> : null}

      <View className="mt-2 flex-row items-center justify-between">
        <Text className="text-sm text-[#5B7285]">
          {travelersLabel(item.travelers)} × {item.currency} {(item.unitPrice ?? 0).toFixed(2)}
        </Text>
        <Price amount={item.subtotal} currency={item.currency} />
      </View>

      {statusLabel ? (
        <Text className={`mt-2 text-sm font-semibold ${cancelledByOperator ? 'text-[#991B1B]' : 'text-[#5B7285]'}`}>
          {statusLabel}
        </Text>
      ) : null}
      {cancelledByOperator && item.cancellationReason ? (
        <Text className="mt-1 text-sm leading-5 text-[#5B7285]">Motivo del operador: {item.cancellationReason}</Text>
      ) : null}
    </View>
  )
}

/** Aviso del tiempo que queda para pagar. Llegar a 00:00 no cambia el estado: lo decide el backend. */
export function CountdownBanner({ label, isTimeUp }: { label: string | null; isTimeUp: boolean }) {
  if (isTimeUp) {
    return (
      <View accessibilityRole="alert" className="rounded-2xl bg-[#E8EEF2] p-4">
        <Text className="text-base font-semibold text-ink">Se venció el tiempo para pagar</Text>
        <Text className="mt-1 text-sm leading-5 text-[#5B7285]">
          Estamos actualizando el estado de la reserva. El cupo retenido se libera en unos instantes.
        </Text>
      </View>
    )
  }

  return (
    <View className="rounded-2xl bg-[#FEF3C7] p-4">
      <Text className="text-base font-semibold text-[#92400E]">Tu lugar está reservado · {label}</Text>
      <Text className="mt-1 text-sm leading-5 text-[#92400E]">
        Si no pagás antes de que termine el tiempo, el cupo se libera.
      </Text>
    </View>
  )
}

/** La fecha más cercana entre las líneas (YYYY-MM-DD ordena bien como texto). */
function earliestItem(items: ReservationItemResponse[]): ReservationItemResponse | undefined {
  return [...items].filter((item) => item.date).sort((a, b) => (a.date! < b.date! ? -1 : 1))[0] ?? items[0]
}

/** Tarjeta de "Mis viajes". Solo datos que devuelve ReservationResponse: no hay imagen ni destino. */
export function TripCard({
  reservation,
  now,
  countdownLabel,
}: {
  reservation: ReservationResponse
  now: number
  countdownLabel?: string | null
}) {
  const router = useRouter()
  const status = displayStatus(reservation, now)
  const { title, extraCount } = reservationTitle(reservation)
  const items = reservation.items ?? []
  const first = earliestItem(items)
  const when = first ? formatItemWhen(extraCount === 0 ? first : { date: first.date }) : null

  // El botón "Pagar" va FUERA del área presionable: un contenedor accesible agrupa a sus hijos (VoiceOver
  // lo lee como un solo elemento), y un botón anidado adentro no se podría alcanzar por separado.
  return (
    <View className="rounded-2xl bg-surface" style={{ elevation: 2 }}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={`${title}. ${status.label}`}
        onPress={() => router.push(`/reservation/${reservation.id}` as Href)}
        className="p-4 active:opacity-90"
      >
        <View className="flex-row items-center justify-between gap-2">
          <StatusBadge status={status} />
          {status.kind === 'PENDING' && countdownLabel ? (
            <Text className="text-sm font-semibold text-[#92400E]">{countdownLabel}</Text>
          ) : null}
        </View>

        <Text className="mt-3 text-base font-semibold text-ink" numberOfLines={2}>
          {title}
          {extraCount > 0 ? <Text className="text-[#5B7285]">{` +${extraCount} más`}</Text> : null}
        </Text>
        {first?.companyName ? (
          <Text className="mt-0.5 text-sm text-[#5B7285]" numberOfLines={1}>
            {first.companyName}
          </Text>
        ) : null}

        <View className="mt-2 flex-row flex-wrap gap-x-3">
          {when ? <Text className="text-sm text-ink">{when}</Text> : null}
          <Text className="text-sm text-[#5B7285]">
            {extraCount > 0 ? `${items.length} servicios` : travelersLabel(first?.travelers)}
          </Text>
        </View>

        <View className="mt-3">
          <TotalsList totals={reservation.totals} />
        </View>
      </Pressable>

      {status.kind === 'PENDING' ? (
        <View className="px-4 pb-4">
          <Button label="Pagar" onPress={() => router.push(`/checkout/${reservation.id}` as Href)} />
        </View>
      ) : null}
    </View>
  )
}

export function TripCardSkeleton() {
  return (
    <View className="rounded-2xl bg-surface p-4" style={{ elevation: 2 }}>
      <Skeleton className="h-5 w-28 rounded-full" />
      <Skeleton className="mt-3 h-4 w-3/4" />
      <Skeleton className="mt-2 h-3 w-40" />
      <Skeleton className="mt-3 h-5 w-24" />
    </View>
  )
}

/** Cabecera de pantalla con botón de volver. Sin historial (deep link) vuelve a `fallback`. */
export function ScreenHeader({ title, fallback = '/trips' }: { title: string; fallback?: Href }) {
  const router = useRouter()

  return (
    <View className="flex-row items-center gap-2 px-3 pb-2 pt-1">
      <Pressable
        accessibilityRole="button"
        accessibilityLabel="Volver"
        onPress={() => (router.canGoBack() ? router.back() : router.replace(fallback))}
        className="h-11 w-11 items-center justify-center rounded-full active:opacity-60"
      >
        <Text className="text-xl text-ink">←</Text>
      </Pressable>
      <Text className="text-lg font-bold text-ink">{title}</Text>
    </View>
  )
}
