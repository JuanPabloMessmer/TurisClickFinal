import type { flightsApi } from '@turisclick/api-client'
import { formatCurrency } from '@turisclick/utils'
import { Briefcase, Plane } from 'lucide-react-native'
import { useState } from 'react'
import { ScrollView, Text, View } from 'react-native'
import { isBookableProviderOption } from '@/features/flights/booking/model'
import { formatDuration, formatTime, stopsLabel } from '@/features/flights/components'
import {
  activeFilterCount,
  applyFilters,
  arrivalDayOffset,
  carriersOf,
  checkedBagsOf,
  NO_FILTERS,
  sliceMinutes,
  TIME_BUCKETS,
  type ResultFilters,
} from '@/features/flights/search/model'
import { SheetModal } from '@/features/flights/search/SheetModal'
import { colors } from '@/theme/colors'
import { Button, Chip, Icon, SegmentedControl, Surface } from '@/ui'

type Option = flightsApi.FlightOptionResponse
type Slice = NonNullable<Option['slices']>[number]

/**
 * De dónde salen los vuelos, dicho arriba de la lista y no en una nota al pie. Con el proveedor simulado
 * los vuelos NO existen: son datos de demostración que TurisClick genera, y una pantalla que los mostrara
 * como vuelos reales estaría mintiendo. Sin el dato (un backend anterior) se asume lo prudente.
 */
export function InventoryBanner({
  mode,
  testMode,
  hasBookable = false,
}: {
  mode?: string | null
  testMode?: boolean
  /** Hay vuelos de operador reservables en la lista: el aviso tiene que distinguirlos de las ofertas simuladas. */
  hasBookable?: boolean
}) {
  if (mode === 'LIVE' || (!mode && !testMode)) return null

  const sandbox = mode === 'SANDBOX'
  return (
    <View
      testID="inventory-banner"
      accessibilityRole="alert"
      className="rounded-md border border-warning-fg/30 bg-warning-bg p-3"
    >
      <Text className="font-ui700 text-label text-warning-fg">
        {sandbox ? 'MODO PRUEBA · no se emiten pasajes' : 'DEMO · vuelos simulados'}
      </Text>
      <Text className="mt-1 font-sans text-caption text-warning-fg">
        {hasBookable
          ? 'Las ofertas de aerolínea son simuladas y no se pueden comprar. Los vuelos con "Seleccionar" los publica un operador de prueba: se reservan con un pago simulado y ningún pasaje es real.'
          : sandbox
            ? 'Ofertas del entorno de prueba del proveedor aéreo. Horarios y precios no corresponden a vuelos comerciales y nada se puede comprar.'
            : 'TurisClick genera estos vuelos para la demostración. No son vuelos reales, no tienen asientos ni se pueden comprar.'}
      </Text>
    </View>
  )
}

/** Una oferta como la muestra cualquier buscador: horarios grandes, duración, escalas, equipaje y total. */
export function FlightOfferCard({
  option,
  passengers,
  demo,
  onSelect,
}: {
  option: Option
  passengers: number
  demo: boolean
  /** Sólo se usa si la opción es un vuelo de operador reservable. */
  onSelect?: (option: Option) => void
}) {
  const bags = checkedBagsOf(option)
  const bookable = isBookableProviderOption(option)
  const provider = option.providerFlight

  return (
    <Surface className="rounded-lg">
      <View className="p-4">
        <View className="flex-row items-center justify-between gap-2">
          <View className="flex-1 flex-row items-center gap-1.5">
            <Icon icon={Plane} size={14} color={colors.primary} />
            <Text className="font-ui600 text-label text-ink" numberOfLines={1}>
              {option.carrierName ?? 'Aerolínea sin informar'}
            </Text>
          </View>
          {demo ? (
            <View className="rounded-full bg-warning-bg px-2 py-0.5">
              <Text className="font-ui700 text-caption text-warning-fg">DEMO</Text>
            </View>
          ) : null}
        </View>

        {(option.slices ?? []).map((slice, index) => (
          <SliceRow
            key={index}
            slice={slice}
            label={(option.slices?.length ?? 0) > 1 ? (index === 0 ? 'Ida' : 'Vuelta') : null}
            // Un vuelo de operador no informa duración: sus horarios son locales y sin huso, así que restarlos
            // daría cualquier cosa entre aeropuertos de husos distintos.
            showDuration={option.source !== 'PROVIDER'}
          />
        ))}

        <View className="mt-3 flex-row items-end justify-between border-t border-border pt-3">
          <View className="flex-1 flex-row items-center gap-1.5">
            <Icon icon={Briefcase} size={14} color={colors.inkMuted} />
            <Text className="font-sans text-caption text-ink-muted">
              {bags === null
                ? 'Equipaje sin informar'
                : bags === 0
                  ? 'Sin valija despachada'
                  : bags === 1
                    ? '1 valija despachada incluida'
                    : `${bags} valijas despachadas incluidas`}
            </Text>
          </View>
          <View className="items-end">
            <Text className="font-display text-heading text-ink">
              {formatCurrency(option.price?.amount ?? 0, option.price?.currency ?? 'USD')}
            </Text>
            <Text className="font-sans text-caption text-ink-muted">
              {passengers === 1 ? 'total' : `total · ${passengers} pasajeros`}
            </Text>
          </View>
        </View>

        {bookable && provider ? (
          <View className="mt-3 gap-3 border-t border-border pt-3">
            <View className="flex-row flex-wrap items-center justify-between gap-2">
              <Text className="flex-1 font-sans text-caption text-ink-muted" numberOfLines={2}>
                Vendido por {provider.operatorName || 'un operador'}
              </Text>
              {(provider.seatsLeft ?? 0) > 0 && (provider.seatsLeft ?? 0) <= 9 ? (
                <Text className="font-ui600 text-caption text-accent-700">
                  {provider.seatsLeft === 1 ? 'Queda 1 asiento' : `Quedan ${provider.seatsLeft} asientos`}
                </Text>
              ) : null}
            </View>
            {onSelect ? (
              <Button
                label="Seleccionar"
                accessibilityLabel={`Seleccionar vuelo de ${option.carrierName ?? 'aerolínea'} vendido por ${provider.operatorName ?? 'un operador'}`}
                onPress={() => onSelect(option)}
              />
            ) : null}
          </View>
        ) : null}
      </View>
    </Surface>
  )
}

function SliceRow({ slice, label, showDuration = true }: { slice: Slice; label: string | null; showDuration?: boolean }) {
  const segments = slice.segments ?? []
  const first = segments[0]
  const last = segments[segments.length - 1]
  const minutes = showDuration ? sliceMinutes(slice) : null
  const offset = arrivalDayOffset(first?.departingAt, last?.arrivingAt)
  const via = segments.slice(0, -1).map((segment) => segment.destinationIata).join(', ')
  const stops = slice.stops ?? Math.max(segments.length - 1, 0)

  return (
    <View className="mt-3">
      {label ? <Text className="font-ui600 text-caption uppercase tracking-wide text-ink-muted">{label}</Text> : null}
      <View className="mt-1 flex-row items-center justify-between gap-3">
        <View>
          <Text className="font-ui700 text-heading text-ink">{first?.departingAt ? formatTime(first.departingAt) : '—'}</Text>
          <Text className="font-sans text-caption text-ink-muted">{slice.originIata}</Text>
        </View>
        <View className="flex-1 items-center">
          <Text className="font-sans text-caption text-ink-muted">{minutes ? formatDuration(minutes) : ''}</Text>
          <View className="my-1 h-px w-full bg-border-control" />
          <Text className={`font-ui600 text-caption ${stops === 0 ? 'text-success-fg' : 'text-ink-muted'}`}>
            {stops === 0 ? 'Directo' : `${stopsLabel(stops)}${via ? ` · ${via}` : ''}`}
          </Text>
        </View>
        <View className="items-end">
          <Text className="font-ui700 text-heading text-ink">
            {last?.arrivingAt ? formatTime(last.arrivingAt) : '—'}
            {offset > 0 ? <Text className="font-ui700 text-caption text-accent-700">{` +${offset}`}</Text> : null}
          </Text>
          <Text className="font-sans text-caption text-ink-muted">{slice.destinationIata}</Text>
        </View>
      </View>
    </View>
  )
}

/**
 * Filtros de resultados. Se aplican en el teléfono sobre lo que ya llegó: refiltrar no vuelve a consultar
 * al proveedor. El botón dice cuántos vuelos quedan, para no aplicar un filtro que vacía la lista sin saberlo.
 */
export function ResultsFilterModal({
  visible,
  options,
  value,
  onApply,
  onClose,
}: {
  visible: boolean
  options: Option[]
  value: ResultFilters
  onApply: (filters: ResultFilters) => void
  onClose: () => void
}) {
  const [draft, setDraft] = useState(value)

  const [lastVisible, setLastVisible] = useState(visible)
  if (visible !== lastVisible) {
    setLastVisible(visible)
    if (visible) setDraft(value)
  }

  const carriers = carriersOf(options)
  const remaining = applyFilters(options, draft).length
  const toggle = <T,>(list: T[], item: T) => (list.includes(item) ? list.filter((x) => x !== item) : [...list, item])

  return (
    <SheetModal
      visible={visible}
      title="Filtros"
      onClose={onClose}
      action={activeFilterCount(draft) > 0 ? { label: 'Limpiar', onPress: () => setDraft(NO_FILTERS) } : undefined}
      footer={
        <Button
          label={remaining === 1 ? 'Ver 1 vuelo' : `Ver ${remaining} vuelos`}
          onPress={() => onApply(draft)}
        />
      }
    >
      <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 32 }}>
        <Text className="mb-2 font-ui700 text-heading text-ink">Escalas</Text>
        <SegmentedControl
          options={[
            { value: 'ANY', label: 'Cualquiera' },
            { value: '0', label: 'Directo' },
            { value: '1', label: 'Hasta 1' },
          ]}
          value={draft.maxStops === null ? 'ANY' : String(draft.maxStops)}
          onChange={(v) => setDraft((d) => ({ ...d, maxStops: v === 'ANY' ? null : Number(v) }))}
        />

        <Text className="mb-2 mt-6 font-ui700 text-heading text-ink">Horario de salida</Text>
        <View className="flex-row flex-wrap gap-2">
          {TIME_BUCKETS.map((bucket) => (
            <Chip
              key={bucket.value}
              label={bucket.label}
              selected={draft.departureTimes.includes(bucket.value)}
              onPress={() => setDraft((d) => ({ ...d, departureTimes: toggle(d.departureTimes, bucket.value) }))}
            />
          ))}
        </View>

        <Text className="mb-2 mt-6 font-ui700 text-heading text-ink">Equipaje</Text>
        <View className="flex-row flex-wrap gap-2">
          <Chip
            label="Con valija despachada"
            selected={draft.withCheckedBag}
            onPress={() => setDraft((d) => ({ ...d, withCheckedBag: !d.withCheckedBag }))}
          />
        </View>

        {carriers.length > 1 ? (
          <>
            <Text className="mb-2 mt-6 font-ui700 text-heading text-ink">Aerolíneas</Text>
            <View className="flex-row flex-wrap gap-2">
              {carriers.map((carrier) => (
                <Chip
                  key={carrier}
                  label={carrier}
                  selected={draft.carriers.includes(carrier)}
                  onPress={() => setDraft((d) => ({ ...d, carriers: toggle(d.carriers, carrier) }))}
                />
              ))}
            </View>
          </>
        ) : null}
      </ScrollView>
    </SheetModal>
  )
}
