import type { FlightBookingResponse, flightsApi } from '@turisclick/api-client'
import { formatCurrency, formatDate } from '@turisclick/utils'
import { Plane, RefreshCw, Ticket } from 'lucide-react-native'
import { Text, View } from 'react-native'
import { colors } from '@/theme/colors'
import { Icon, StateBadge } from '@/ui'

/**
 * Piezas visuales de vuelo compartidas por la ficha del paquete, el checkout y Mis viajes. Están acá y no
 * duplicadas en cada pantalla por una razón de producto, no de prolijidad: un horario, una escala o un
 * precio tienen que leerse igual en los tres lugares, porque es la misma compra.
 */

/** `currency` admite null porque así lo declara el esquema generado del backend. */
export type Money = { amount?: number; currency?: string | null } | null | undefined
type Slice = NonNullable<NonNullable<flightsApi.FlightQuoteOptionResponse['slices']>>[number]

/** Hora local del aeropuerto: el backend la manda sin huso justamente para que no se desplace. */
export function formatTime(value: string) {
  const time = value.includes('T') ? value.split('T')[1] : value
  return time.slice(0, 5)
}

export function formatDuration(minutes: number) {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return hours === 0 ? `${rest} min` : rest === 0 ? `${hours} h` : `${hours} h ${rest} min`
}

export function stopsLabel(stops?: number) {
  if (stops === 0) return 'directo'
  return stops === 1 ? '1 escala' : `${stops} escalas`
}

export function money(value: Money) {
  return formatCurrency(value?.amount ?? 0, value?.currency ?? 'USD')
}

/** Los tramos de una oferta: ida y, si corresponde, vuelta. */
export function FlightItinerary({ slices }: { slices: Slice[] }) {
  return (
    <>
      {slices.map((slice, index) => {
        const segments = slice.segments ?? []
        const first = segments[0]
        const last = segments[segments.length - 1]

        return (
          <View key={index} className={index === 0 ? '' : 'mt-2 border-t border-border pt-2'}>
            <Text className="font-ui600 text-label text-ink">
              {slice.originIata} → {slice.destinationIata}
            </Text>
            <Text className="mt-0.5 font-sans text-caption text-ink-muted">
              {first?.departingAt ? formatTime(first.departingAt) : '—'} ·{' '}
              {last?.arrivingAt ? formatTime(last.arrivingAt) : '—'} · {stopsLabel(slice.stops)}
              {slice.durationMinutes ? ` · ${formatDuration(slice.durationMinutes)}` : ''}
            </Text>
            {first?.carrierName ? (
              <Text className="mt-0.5 font-sans text-caption text-ink-muted">
                {first.carrierName}
                {first.flightNumber ? ` ${first.carrierIata}${first.flightNumber}` : ''}
              </Text>
            ) : null}
          </View>
        )
      })}
    </>
  )
}

/**
 * Vuelo, paquete y total. El total sólo aparece si las dos monedas coinciden: cuando no, se dice por qué
 * no hay una suma en vez de inventar un tipo de cambio.
 */
export function FlightPriceRows({
  flightPrice,
  packagePrice,
  combinedTotal,
  totalLabel = 'Total por persona',
}: {
  flightPrice: Money
  packagePrice: Money
  combinedTotal: Money
  totalLabel?: string
}) {
  return (
    <View className="mt-3 border-t border-border pt-3">
      <View className="flex-row items-baseline justify-between">
        <Text className="font-sans text-label text-ink-muted">Vuelo</Text>
        <Text className="font-ui700 text-heading text-ink">{money(flightPrice)}</Text>
      </View>
      <View className="mt-1 flex-row items-baseline justify-between">
        <Text className="font-sans text-label text-ink-muted">Paquete</Text>
        <Text className="font-sans text-label text-ink">{money(packagePrice)}</Text>
      </View>

      {combinedTotal ? (
        <View className="mt-2 flex-row items-baseline justify-between border-t border-border pt-2">
          <Text className="font-ui600 text-label text-ink">{totalLabel}</Text>
          <Text className="font-ui700 text-title text-ink">{money(combinedTotal)}</Text>
        </View>
      ) : (
        <Text className="mt-2 border-t border-border pt-2 font-sans text-caption text-ink-muted">
          El paquete y el vuelo se cobran en monedas distintas, así que se muestran por separado.
        </Text>
      )}
    </View>
  )
}

/** El resultado de revalidar, con el tono que corresponde: confirmar no es lo mismo que avisar un cambio. */
export function RevalidationNotice({ revalidation }: { revalidation: flightsApi.FlightQuoteRevalidationResponse }) {
  const tone =
    revalidation.outcome === 'UNCHANGED' ? 'success' : revalidation.outcome === 'PRICE_CHANGED' ? 'warning' : 'danger'

  const label =
    revalidation.outcome === 'UNCHANGED'
      ? 'Precio confirmado'
      : revalidation.outcome === 'PRICE_CHANGED'
        ? 'Cambió el precio'
        : revalidation.outcome === 'EXPIRED'
          ? 'Cotización vencida'
          : 'Ya no disponible'

  return (
    <View className="mt-3 rounded-md bg-background p-3">
      <StateBadge label={label} tone={tone} />
      <Text className="mt-2 font-sans text-label text-ink">{revalidation.message}</Text>
      {revalidation.outcome === 'PRICE_CHANGED' && revalidation.currentPrice ? (
        <Text className="mt-1 font-sans text-caption text-ink-muted">
          Antes {money(revalidation.previousPrice)} · ahora {money(revalidation.currentPrice)}
        </Text>
      ) : null}
    </View>
  )
}

/** Encabezado de sección con el ícono del avión. Vectorial, nunca un emoji. */
export function FlightSectionHeader({ title }: { title: string }) {
  return (
    <View className="mb-3 flex-row items-center gap-2">
      <Icon icon={Plane} size={18} color={colors.primary} />
      <Text className="font-ui700 text-title text-ink">{title}</Text>
    </View>
  )
}

const FLIGHT_TONES: Record<string, 'success' | 'warning' | 'danger' | 'neutral'> = {
  CONFIRMED: 'success',
  PENDING: 'neutral',
  ORDERING: 'warning',
  RECONCILIATION_REQUIRED: 'warning',
  FAILED: 'danger',
  CANCELLED: 'danger',
}

const FLIGHT_LABELS: Record<string, string> = {
  CONFIRMED: 'Pasaje emitido',
  PENDING: 'Pasaje pendiente',
  ORDERING: 'Emitiendo el pasaje',
  RECONCILIATION_REQUIRED: 'Confirmando con la aerolínea',
  FAILED: 'No se pudo emitir',
  CANCELLED: 'Pasaje cancelado',
}

/**
 * El vuelo de una reserva, tal como queda guardado. Muestra el itinerario congelado y el localizador: es
 * lo que la persona necesita para entender qué compró y para presentarse a volar, incluso meses después,
 * cuando la oferta original del proveedor ya no exista.
 */
export function FlightBookingCard({ flight }: { flight: FlightBookingResponse }) {
  const status = flight.status ?? 'PENDING'

  return (
    <View className="rounded-lg border border-border bg-surface p-4">
      <View className="flex-row items-center justify-between gap-2">
        <View className="flex-row items-center gap-2">
          <Icon icon={Plane} size={16} color={colors.primary} />
          <Text className="font-ui600 text-label text-ink">Vuelo</Text>
        </View>
        <StateBadge label={FLIGHT_LABELS[status] ?? 'Vuelo'} tone={FLIGHT_TONES[status] ?? 'neutral'} />
      </View>

      <Text className="mt-3 font-ui700 text-heading text-ink">
        {flight.originLabel ?? flight.originIata} → {flight.destinationLabel ?? flight.destinationIata}
      </Text>

      <Text className="mt-1 font-sans text-label text-ink-muted">
        Ida {flight.outboundDate ? formatDate(flight.outboundDate) : '—'}
        {flight.inboundDate ? ` · vuelta ${formatDate(flight.inboundDate)}` : ''}
      </Text>

      {flight.carrierName ? (
        <Text className="mt-1 font-sans text-label text-ink-muted">
          {flight.carrierName}
          {flight.outboundFlightNumber ? ` ${flight.carrierIata}${flight.outboundFlightNumber}` : ''}
          {flight.outboundDepartureAt ? ` · sale ${formatTime(flight.outboundDepartureAt)}` : ''}
          {flight.outboundArrivalAt ? ` · llega ${formatTime(flight.outboundArrivalAt)}` : ''}
        </Text>
      ) : null}

      {flight.inboundFlightNumber && flight.inboundDepartureAt ? (
        <Text className="mt-0.5 font-sans text-label text-ink-muted">
          Vuelta {flight.carrierIata}
          {flight.inboundFlightNumber} · sale {formatTime(flight.inboundDepartureAt)}
          {flight.inboundArrivalAt ? ` · llega ${formatTime(flight.inboundArrivalAt)}` : ''}
        </Text>
      ) : null}

      <View className="mt-3 flex-row items-baseline justify-between border-t border-border pt-3">
        <Text className="font-sans text-label text-ink-muted">
          {flight.travelers} {flight.travelers === 1 ? 'pasajero' : 'pasajeros'}
        </Text>
        <Text className="font-ui700 text-heading text-ink">{money(flight.price)}</Text>
      </View>

      {flight.bookingReference ? (
        <View className="mt-3 flex-row items-center gap-2 rounded-md bg-background p-3">
          <Icon icon={Ticket} size={14} color={colors.inkMuted} />
          <Text className="font-sans text-label text-ink">
            Localizador <Text className="font-ui700">{flight.bookingReference}</Text>
          </Text>
        </View>
      ) : null}

      {flight.inProgress ? (
        <View className="mt-3 flex-row items-start gap-2 rounded-md bg-warning-bg p-3">
          <Icon icon={RefreshCw} size={14} color={colors.warningFg} />
          <Text className="flex-1 font-sans text-label text-warning-fg">{flight.statusMessage}</Text>
        </View>
      ) : status === 'FAILED' ? (
        <Text className="mt-3 font-sans text-label text-danger-fg">{flight.statusMessage}</Text>
      ) : null}
    </View>
  )
}
