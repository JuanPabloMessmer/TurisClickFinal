import type { ReservationItemResponse } from '@turisclick/api-client'
import { formatCurrency, formatDate } from '@turisclick/utils'
import { Plane, Ticket } from 'lucide-react-native'
import { Text, View } from 'react-native'
import { formatTime } from '@/features/flights/components'
import { issuanceDisplay, isDemoFlight, passengersSummaryOf } from '@/features/flights/booking/model'
import { cabinLabel } from '@/features/flights/search/model'
import { colors } from '@/theme/colors'
import { Icon, Price, StateBadge } from '@/ui'

/** Rótulo que acompaña a todo vuelo de operador en la demo: un pasaje de prueba no es un pasaje real. */
export function DemoTag() {
  return (
    <View testID="demo-tag" className="rounded-full bg-warning-bg px-2 py-0.5">
      <Text className="font-ui700 text-caption text-warning-fg">DEMO</Text>
    </View>
  )
}

const PASSENGER_TYPE_LABEL: Record<string, string> = { ADULT: 'Adulto', CHILD: 'Niño', INFANT: 'Infante' }

/**
 * Un vuelo de operador dentro de una reserva: ruta, horarios, aerolínea, quién lo vende, pasajeros y en qué
 * está el pasaje. El precio va como subtotal y no como "N × precio": adultos, niños e infantes pagan tarifas
 * distintas, así que esa multiplicación no daría el total.
 */
export function ProviderFlightItemCard({
  item,
  legLabel,
}: {
  item: ReservationItemResponse
  /** "Ida" o "Vuelta" cuando la reserva tiene dos vuelos. */
  legLabel?: string | null
}) {
  const flight = item.flight!
  const issuance = issuanceDisplay(flight, item.status)
  const departureDay = flight.departureAt?.slice(0, 10)
  const tickets = flight.ticketNumbers ?? []
  const failed = issuance.kind === 'FAILED'

  return (
    <View testID="provider-flight-item" className="rounded-2xl bg-surface p-4" style={{ elevation: 1 }}>
      <View className="flex-row items-center justify-between gap-2">
        <View className="flex-row items-center gap-1.5">
          <Icon icon={Plane} size={14} color={colors.primary} />
          <Text className="text-xs font-medium uppercase tracking-wide text-secondary">
            {legLabel ? `Vuelo · ${legLabel}` : 'Vuelo'}
          </Text>
        </View>
        {isDemoFlight(flight) ? <DemoTag /> : null}
      </View>

      <Text className={`mt-1 text-base font-semibold ${failed ? 'text-[#5B7285] line-through' : 'text-ink'}`}>
        {flight.originLabel || flight.originIata} → {flight.destinationLabel || flight.destinationIata}
      </Text>

      <Text className="mt-2 text-sm text-ink">
        {departureDay ? formatDate(departureDay) : ''}
        {flight.departureAt ? ` · ${formatTime(flight.departureAt)}` : ''}
        {flight.arrivalAt ? ` → ${formatTime(flight.arrivalAt)}` : ''}
      </Text>
      <Text className="mt-0.5 text-sm text-[#5B7285]">
        {flight.carrierName}
        {flight.flightNumber ? ` ${flight.carrierIata ?? ''}${flight.flightNumber}` : ''} · {cabinLabel(flight.cabinClass)}
        {flight.checkedBags ? ` · ${flight.checkedBags} ${flight.checkedBags === 1 ? 'valija' : 'valijas'}` : ' · sin valija'}
      </Text>
      {flight.operatorName ? (
        <Text className="mt-0.5 text-sm text-[#5B7285]">Vendido por {flight.operatorName}</Text>
      ) : null}

      <View className="mt-3 gap-0.5" accessibilityLabel="Pasajeros">
        <Text className="text-sm font-semibold text-ink">{passengersSummaryOf(flight)}</Text>
        {(flight.passengers ?? []).map((passenger, index) => (
          <Text key={index} className="text-sm text-[#5B7285]">
            {passenger.givenName} {passenger.familyName} · {PASSENGER_TYPE_LABEL[passenger.type ?? ''] ?? 'Pasajero'}
          </Text>
        ))}
      </View>

      <View className="mt-3 flex-row items-center justify-between">
        <Text className="text-sm text-[#5B7285]">Subtotal</Text>
        <Price amount={item.subtotal} currency={item.currency} />
      </View>

      <View testID="issuance-status" className="mt-3 gap-1.5 rounded-md bg-background p-3">
        <StateBadge label={issuance.label} tone={issuance.tone} />
        <Text className="text-sm leading-5 text-ink">{issuance.message}</Text>

        {issuance.kind === 'ISSUED' && tickets.length > 0 ? (
          <View className="mt-1 gap-0.5">
            {/* Un pasaje por pasajero, infantes incluidos, en el orden de `passengers` (contrato B2). */}
            {tickets.map((ticket, index) => {
              const passenger = flight.passengers?.[index]
              return (
                <View key={ticket} className="flex-row items-center gap-1.5">
                  <Icon icon={Ticket} size={14} color={colors.inkMuted} />
                  <Text className="text-sm text-ink">
                    {passenger ? `${passenger.givenName} ${passenger.familyName} · ` : ''}Pasaje {ticket}
                  </Text>
                </View>
              )
            })}
            {flight.bookingReference ? (
              <Text className="text-sm text-[#5B7285]">Localizador {flight.bookingReference}</Text>
            ) : null}
          </View>
        ) : null}

        {failed && (item.refundedAmount ?? 0) > 0 ? (
          <Text className="text-sm font-semibold text-[#166534]">
            Reembolso total: {formatCurrency(item.refundedAmount ?? 0, item.currency ?? 'BOB')}
            {isDemoFlight(flight) ? ' (simulado)' : ''}
          </Text>
        ) : null}

        {isDemoFlight(flight) && issuance.kind === 'ISSUED' ? (
          <Text className="text-xs leading-4 text-warning-fg">
            Demostración: el número lo cargó un operador de prueba y no es un pasaje real.
          </Text>
        ) : null}
      </View>
    </View>
  )
}
