import { useMutation } from '@tanstack/react-query'
import type { PackageResponse } from '@turisclick/api-client'
import { flightsApi } from '@turisclick/api-client'
import { formatDate } from '@turisclick/utils'
import { useEffect, useState } from 'react'
import { Pressable, Text, View } from 'react-native'
import { FlightItinerary, FlightPriceRows, FlightSectionHeader, money, type Money } from '@/features/flights/components'
import { toApiError } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'
import { Button, Chip, FormError, Skeleton } from '@/ui'

type Quote = flightsApi.PackageFlightQuoteResponse
type Option = NonNullable<Quote['options']>[number]

export interface FlightSelection {
  quoteId: string
  flightPrice: NonNullable<Money>
  combinedTotal?: Money
}

/**
 * Elegir el vuelo durante la reserva.
 *
 * Va acá y no en la ficha porque el precio del pasaje depende de dos cosas que se deciden en este paso: la
 * salida y cuántos viajan. Cotizar antes de saberlas daría un número que después cambia, que es la forma más
 * segura de perder la confianza de alguien.
 *
 * La búsqueda es un botón explícito y no automática: cada búsqueda consulta a una aerolínea de verdad, y
 * disparar una por cada toque en el calendario sería castigar al proveedor por nuestra comodidad.
 */
export function FlightPicker({
  pkg,
  availabilityId,
  travelers,
  selection,
  onSelect,
}: {
  pkg: PackageResponse
  availabilityId: string | null
  travelers: number
  selection: FlightSelection | null
  onSelect: (selection: FlightSelection | null) => void
}) {
  const [origin, setOrigin] = useState<string | null>(null)
  const [quote, setQuote] = useState<Quote | null>(null)
  const [error, setError] = useState<string | null>(null)

  const origins = pkg.flightOrigins ?? []

  // Cambiar de salida, de origen o de cantidad invalida la cotización: ya no es la misma compra.
  useEffect(() => {
    setQuote(null)
    onSelect(null)
    setError(null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [availabilityId, travelers, origin])

  const search = useMutation({
    mutationFn: () =>
      flightsApi.quotePackageFlights(httpClient, pkg.id!, {
        originIata: origin!,
        packageAvailabilityId: availabilityId!,
        travelers,
      }),
    retry: false,
    onSuccess: (result) => {
      setQuote(result)
      setError(null)
      const first = result.options?.[0]
      if (first?.quoteId) {
        onSelect({
          quoteId: first.quoteId,
          flightPrice: first.flightPrice ?? {},
          combinedTotal: first.combinedTotal,
        })
      }
    },
    onError: (searchError) => setError(toApiError(searchError).message),
  })

  const options = quote?.options ?? []
  const canSearch = Boolean(origin) && Boolean(availabilityId) && !search.isPending

  return (
    <View className="mt-8">
      <FlightSectionHeader title="Vuelo" />

      <Text className="font-sans text-body text-ink-muted">
        Este paquete incluye el pasaje
        {pkg.flightDestinationLabel ? ` a ${pkg.flightDestinationLabel}` : ''}. Elegí desde dónde salís y
        buscamos los vuelos de esas fechas con su precio real.
      </Text>

      <Text className="mt-4 font-ui600 text-label text-ink">¿Desde dónde salís?</Text>
      <View className="mt-2 flex-row flex-wrap gap-2">
        {origins.map((airport) => (
          <Chip
            key={airport.iata}
            label={airport.label ?? airport.iata ?? ''}
            selected={origin === airport.iata}
            onPress={() => setOrigin(airport.iata!)}
          />
        ))}
      </View>

      {!availabilityId ? (
        <Text className="mt-3 font-sans text-label text-ink-muted">Elegí primero la salida del paquete.</Text>
      ) : null}

      {quote === null ? (
        <View className="mt-4 self-start">
          <Button
            label="Buscar vuelos"
            variant="outline"
            disabled={!canSearch}
            loading={search.isPending}
            onPress={() => search.mutate()}
          />
        </View>
      ) : null}

      {search.isPending ? (
        <View className="mt-4 gap-2" accessibilityLabel="Buscando vuelos">
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-24 w-full" />
        </View>
      ) : null}

      {error ? <View className="mt-4"><FormError message={error} /></View> : null}

      {quote && options.length === 0 ? (
        <View className="mt-4">
          <Text className="font-sans text-body text-ink">
            {quote.notice ?? 'No encontramos vuelos para esas fechas.'}
          </Text>
          <Text className="mt-1 font-sans text-label text-ink-muted">
            Probá con otra salida del paquete o con otra ciudad de origen.
          </Text>
          <View className="mt-3 self-start">
            <Button label="Buscar de nuevo" variant="outline" onPress={() => search.mutate()} />
          </View>
        </View>
      ) : null}

      {quote && options.length > 0 ? (
        <View className="mt-4 gap-3">
          <Text className="font-sans text-label text-ink-muted">
            {quote.originLabel} → {quote.destinationLabel} · ida {formatDate(quote.outboundDate!)}
            {quote.inboundDate ? ` · vuelta ${formatDate(quote.inboundDate)}` : ''}
          </Text>

          <View accessibilityRole="radiogroup" className="gap-3">
            {options.map((option) => (
              <SelectableFlight
                key={option.quoteId}
                option={option}
                packagePrice={quote.packagePrice}
                selected={selection?.quoteId === option.quoteId}
                onPress={() =>
                  onSelect({
                    quoteId: option.quoteId!,
                    flightPrice: option.flightPrice ?? {},
                    combinedTotal: option.combinedTotal,
                  })
                }
              />
            ))}
          </View>

          {quote.testMode ? (
            <Text className="font-sans text-caption text-ink-muted">
              Inventario de prueba del proveedor: los horarios y precios no corresponden a vuelos comerciales
              reales.
            </Text>
          ) : null}
        </View>
      ) : null}
    </View>
  )
}

function SelectableFlight({
  option,
  packagePrice,
  selected,
  onPress,
}: {
  option: Option
  packagePrice: Quote['packagePrice']
  selected: boolean
  onPress: () => void
}) {
  const first = option.slices?.[0]?.segments?.[0]

  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityState={{ selected, checked: selected }}
      accessibilityLabel={`${first?.carrierName ?? 'Vuelo'}, ${money(option.flightPrice)}`}
      onPress={onPress}
      className={`rounded-md border p-3 active:opacity-80 ${
        selected ? 'border-primary bg-primary/5' : 'border-border bg-surface'
      }`}
    >
      <View className="flex-row items-start gap-3">
        <View
          className={`mt-0.5 h-5 w-5 items-center justify-center rounded-full border-2 ${
            selected ? 'border-primary' : 'border-border-control'
          }`}
        >
          {selected ? <View className="h-2.5 w-2.5 rounded-full bg-primary" /> : null}
        </View>
        <View className="flex-1">
          <FlightItinerary slices={option.slices ?? []} />
          <FlightPriceRows
            flightPrice={option.flightPrice}
            packagePrice={packagePrice}
            combinedTotal={option.combinedTotal}
            totalLabel="Total"
          />
        </View>
      </View>
    </Pressable>
  )
}
