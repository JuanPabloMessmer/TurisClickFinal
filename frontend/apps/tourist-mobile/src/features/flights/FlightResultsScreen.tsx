import type { flightsApi } from '@turisclick/api-client'
import { formatCurrency, formatDate } from '@turisclick/utils'
import { useRouter } from 'expo-router'
import { SlidersHorizontal } from 'lucide-react-native'
import { useMemo, useState } from 'react'
import { Pressable, ScrollView, Text, View } from 'react-native'
import { useFlightSearchResults } from '@/features/flights/api'
import { bookingParams, isBookableProviderOption, isDemoOffer } from '@/features/flights/booking/model'
import {
  activeFilterCount,
  applyFilters,
  cabinLabel,
  NO_FILTERS,
  passengersSummary,
  SORTS,
  sortOptions,
  type ResultFilters,
  type SortKey,
} from '@/features/flights/search/model'
import { FlightOfferCard, InventoryBanner, ResultsFilterModal } from '@/features/flights/search/results'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { Badge, Button, Chip, EmptyState, ErrorState, Icon, Skeleton, Surface } from '@/ui'

/**
 * Resultados de una búsqueda de vuelos: orden, filtros y las ofertas.
 *
 * "Seleccionar" aparece sólo en los vuelos que publica un operador autorizado y que el backend marca como
 * reservables. Las ofertas de aerolínea (el proveedor simulado) se muestran para comparar, rotuladas DEMO, y
 * nunca se ofrecen para comprar. Mirar no exige sesión: la sesión se pide al empezar la reserva.
 */
export function FlightResultsScreen({ request }: { request: flightsApi.SearchFlightsRequest | null }) {
  const router = useRouter()
  const results = useFlightSearchResults(request)
  const [sort, setSort] = useState<SortKey>('PRICE')
  const [filters, setFilters] = useState<ResultFilters>(NO_FILTERS)
  const [filtersOpen, setFiltersOpen] = useState(false)

  const data = results.data
  const options = useMemo(() => data?.options ?? [], [data])
  const visible = useMemo(() => sortOptions(applyFilters(options, filters), sort), [options, filters, sort])

  if (!request) {
    return (
      <View className="px-5 pt-6">
        <EmptyState title="Búsqueda incompleta" message="Volvé al buscador y elegí origen, destino y fechas." />
      </View>
    )
  }

  const passengers = {
    adults: request.adults ?? 1,
    childrenAges: request.childrenAges ?? [],
    infants: request.infants ?? 0,
  }
  const totalPassengers = passengers.adults + passengers.childrenAges.length + passengers.infants
  const hasBookable = options.some(isBookableProviderOption)
  const bookableDemo = options.some((option) => isBookableProviderOption(option) && isDemoOffer(option))

  const onSelect = (option: flightsApi.FlightOptionResponse) =>
    router.push({ pathname: '/book/flight', params: bookingParams(request, option) })

  return (
    <ScrollView contentContainerStyle={{ paddingBottom: 32 }}>
      <View className="px-5">
        <Surface className="rounded-lg">
          <View className="flex-row items-center gap-3 p-3">
            <View className="flex-1">
              <Text className="font-ui700 text-heading text-ink" numberOfLines={1}>
                {data?.originLabel ?? request.originIata} → {data?.destinationLabel ?? request.destinationIata}
              </Text>
              <Text className="mt-0.5 font-sans text-caption text-ink-muted" numberOfLines={2}>
                {request.departureDate ? formatDate(request.departureDate) : ''}
                {request.returnDate ? ` – ${formatDate(request.returnDate)}` : ' · sólo ida'} ·{' '}
                {passengersSummary(passengers)} · {cabinLabel(request.cabinClass)}
              </Text>
            </View>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Editar búsqueda"
              onPress={() => router.back()}
              className="h-11 justify-center rounded-md px-3 active:opacity-60"
            >
              <Text className="font-ui600 text-label text-primary">Editar</Text>
            </Pressable>
          </View>
        </Surface>
      </View>

      {results.isPending ? (
        <View className="mt-4 gap-3 px-5">
          <Skeleton className="h-36 w-full" />
          <Skeleton className="h-36 w-full" />
          <Skeleton className="h-36 w-full" />
        </View>
      ) : results.isError ? (
        <View className="mt-6 px-5">
          <ErrorState message={toApiError(results.error).message} onRetry={() => void results.refetch()} />
        </View>
      ) : data ? (
        <>
          <View className="mt-4 px-5">
            <InventoryBanner mode={data.inventoryMode} testMode={data.testMode} hasBookable={hasBookable} bookableDemo={bookableDemo} />
          </View>

          {data.notice ? (
            <View className="mt-4 px-5">
              <EmptyState title="Sin vuelos para esa búsqueda" message={data.notice} />
            </View>
          ) : null}

          {options.length > 0 ? (
            <>
              <ScrollView horizontal showsHorizontalScrollIndicator={false} className="mt-4">
                <View className="flex-row items-center gap-2 px-5">
                  {SORTS.map((option) => (
                    <Chip key={option.value} label={option.label} selected={sort === option.value} onPress={() => setSort(option.value)} />
                  ))}
                  <Pressable
                    accessibilityRole="button"
                    accessibilityLabel={`Filtros${activeFilterCount(filters) ? `, ${activeFilterCount(filters)} activos` : ''}`}
                    onPress={() => setFiltersOpen(true)}
                    className="h-11 flex-row items-center rounded-full border border-border-control bg-surface px-4 active:opacity-60"
                  >
                    <Icon icon={SlidersHorizontal} size={16} color={colors.primary} />
                    <Text className="ml-1.5 font-ui600 text-label text-primary">Filtros</Text>
                    <Badge count={activeFilterCount(filters)} />
                  </Pressable>
                </View>
              </ScrollView>

              <Text className="mt-3 px-5 font-sans text-caption text-ink-muted">
                {visible.length === options.length
                  ? `${options.length} ${options.length === 1 ? 'vuelo' : 'vuelos'}`
                  : `${visible.length} de ${options.length} vuelos`}
              </Text>

              {visible.length === 0 ? (
                <View className="mt-3 px-5">
                  <EmptyState title="Ningún vuelo cumple los filtros" message="Quitá alguno para ver más opciones." />
                  <View className="mt-3">
                    <Button label="Quitar filtros" variant="outline" onPress={() => setFilters(NO_FILTERS)} />
                  </View>
                </View>
              ) : (
                <View className="mt-3 gap-3 px-5">
                  {visible.map((option, index) => (
                    <FlightOfferCard key={index} option={option} passengers={totalPassengers} demo={isDemoOffer(option, data.inventoryMode)} onSelect={onSelect} />
                  ))}
                </View>
              )}
            </>
          ) : null}

          <View className="mt-8 px-5">
            <Text className="font-display text-heading text-ink">
              {hasBookable ? '¿Preferís un paquete con vuelo?' : '¿Se puede comprar este vuelo?'}
            </Text>
            <Text className="mt-1 font-sans text-body text-ink-muted">
              {hasBookable
                ? 'Los vuelos con "Seleccionar" se compran sueltos. También podés llevar el vuelo dentro de un paquete de TurisClick, y el operador se hace cargo de los dos.'
                : 'Todavía no: la compra de vuelos sueltos no está disponible. Hoy el vuelo se compra dentro de un paquete de TurisClick, y el operador se hace cargo de los dos.'}
            </Text>

            {(data.packages ?? []).length === 0 ? (
              <View className="mt-4">
                <EmptyState
                  title="Todavía no hay paquetes en esta ruta"
                  message="Ningún operador publicó un paquete con vuelo entre estos dos aeropuertos."
                />
              </View>
            ) : (
              <View className="mt-4 gap-3">
                {(data.packages ?? []).map((item) => (
                  <Pressable
                    key={item.id}
                    accessibilityRole="button"
                    accessibilityLabel={`Ver ${item.title}`}
                    onPress={() => router.push(`/package/${item.id}`)}
                    className="active:opacity-70"
                  >
                    <Surface className="rounded-lg">
                      <View className="flex-row items-center justify-between gap-3 p-4">
                        <View className="flex-1">
                          <Text className="font-ui600 text-label text-ink" numberOfLines={2}>
                            {item.title}
                          </Text>
                          <Text className="mt-0.5 font-sans text-caption text-ink-muted">
                            {item.destinationName} · {item.durationDays} días
                          </Text>
                        </View>
                        <Text className="font-display text-heading text-ink">
                          {formatCurrency(item.price?.amount ?? 0, item.price?.currency ?? 'BOB')}
                        </Text>
                      </View>
                    </Surface>
                  </Pressable>
                ))}
              </View>
            )}
          </View>
        </>
      ) : null}

      <ResultsFilterModal
        visible={filtersOpen}
        options={options}
        value={filters}
        onApply={(next) => {
          setFilters(next)
          setFiltersOpen(false)
        }}
        onClose={() => setFiltersOpen(false)}
      />
    </ScrollView>
  )
}
