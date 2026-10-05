import { useMutation } from '@tanstack/react-query'
import type { PackageAvailabilityResponse, PackageResponse } from '@turisclick/api-client'
import { flightsApi } from '@turisclick/api-client'
import { formatCurrency, formatDate } from '@turisclick/utils'
import { Plane, RefreshCw } from 'lucide-react-native'
import { useState } from 'react'
import { Pressable, Text, View } from 'react-native'
import { toApiError } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'
import { colors } from '@/theme/colors'
import { Button, Chip, Icon, Skeleton, StateBadge, Surface } from '@/ui'

type Quote = flightsApi.PackageFlightQuoteResponse
type Option = NonNullable<Quote['options']>[number]

/**
 * El vuelo de un paquete, en la ficha del turista.
 *
 * Regla de producto que esta pantalla hace visible: **el pasaje no tiene precio hasta que se cotiza**.
 * Por eso no se muestra un número hasta que la persona busca, y cuando se muestra, se dice de cuándo es
 * y hasta cuándo vale. Nada de "desde" disfrazado de precio final.
 */
export function PackageFlightSection({
  pkg,
  availabilities,
}: {
  pkg: PackageResponse
  availabilities: PackageAvailabilityResponse[]
}) {
  const [origin, setOrigin] = useState<string | null>(null)
  const [quote, setQuote] = useState<Quote | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [revalidated, setRevalidated] = useState<Record<string, flightsApi.FlightQuoteRevalidationResponse>>({})

  // La primera salida con cupo: es la que el turista ve primero en "Próximas salidas", así que es la
  // que se cotiza por defecto.
  const departure = availabilities.find((a) => (a.availableSlots ?? 0) > 0) ?? availabilities[0]

  const search = useMutation({
    mutationFn: (originIata: string) =>
      flightsApi.quotePackageFlights(httpClient, pkg.id!, {
        originIata,
        packageAvailabilityId: departure!.id!,
        travelers: 1,
      }),
    onSuccess: (result) => {
      setQuote(result)
      setError(null)
    },
    onError: (searchError) => setError(toApiError(searchError).message),
  })

  const revalidate = useMutation({
    mutationFn: (quoteId: string) => flightsApi.revalidateFlightQuote(httpClient, quoteId),
    onSuccess: (result) => setRevalidated((current) => ({ ...current, [result.quoteId!]: result })),
    onError: (revalidateError) => setError(toApiError(revalidateError).message),
  })

  if (!pkg.includesFlight || !departure) return null

  return (
    <View className="mt-8">
      <View className="mb-3 flex-row items-center gap-2">
        <Icon icon={Plane} size={18} color={colors.primary} />
        <Text className="font-ui700 text-title text-ink">Vuelo incluido</Text>
      </View>

      <Surface className="rounded-lg border border-border p-4" level="flat">
        <Text className="font-sans text-body text-ink-muted">
          El pasaje se busca con fechas y precios reales al momento de reservar. Elegí desde dónde salís y
          te mostramos las opciones vigentes.
        </Text>

        <Text className="mt-4 font-ui600 text-label text-ink">¿Desde dónde salís?</Text>
        <View className="mt-2 flex-row flex-wrap gap-2">
          <OriginPicker
            pkg={pkg}
            selected={origin}
            onPick={(code) => {
              setOrigin(code)
              setQuote(null)
              setRevalidated({})
              search.mutate(code)
            }}
          />
        </View>

        {search.isPending ? (
          <View className="mt-4 gap-2">
            <Skeleton className="h-20 w-full" />
            <Skeleton className="h-20 w-full" />
          </View>
        ) : null}

        {error ? (
          <View className="mt-4 rounded-md bg-danger-bg p-3">
            <Text className="font-sans text-label text-danger-fg">{error}</Text>
          </View>
        ) : null}

        {quote ? (
          <QuoteResult
            quote={quote}
            revalidated={revalidated}
            onRevalidate={(quoteId) => revalidate.mutate(quoteId)}
            revalidatingId={revalidate.isPending ? revalidate.variables : null}
            onSearchAgain={() => {
              setQuote(null)
              setRevalidated({})
            }}
          />
        ) : null}
      </Surface>
    </View>
  )
}

/**
 * Orígenes que el operador habilitó. Se piden a la regla del paquete y no se inventan: si el operador
 * sólo opera desde Santa Cruz, no se ofrece salir de La Paz.
 */
function OriginPicker({
  pkg,
  selected,
  onPick,
}: {
  pkg: PackageResponse
  selected: string | null
  onPick: (iata: string) => void
}) {
  const origins = pkg.flightOrigins ?? []

  if (origins.length === 0) {
    return (
      <Text className="font-sans text-label text-ink-muted">
        El operador todavía no publicó desde qué ciudades sale este viaje.
      </Text>
    )
  }

  return (
    <>
      {origins.map((airport) => (
        <Chip
          key={airport.iata}
          label={airport.label ?? airport.iata ?? ''}
          selected={selected === airport.iata}
          onPress={() => onPick(airport.iata!)}
        />
      ))}
    </>
  )
}

function QuoteResult({
  quote,
  revalidated,
  revalidatingId,
  onRevalidate,
  onSearchAgain,
}: {
  quote: Quote
  revalidated: Record<string, flightsApi.FlightQuoteRevalidationResponse>
  revalidatingId: string | null
  onRevalidate: (quoteId: string) => void
  onSearchAgain: () => void
}) {
  const options = quote.options ?? []

  if (options.length === 0) {
    return (
      <View className="mt-4">
        <Text className="font-sans text-body text-ink">{quote.notice ?? 'No encontramos vuelos para esas fechas.'}</Text>
        <View className="mt-3 self-start">
          <Button label="Buscar de nuevo" variant="outline" onPress={onSearchAgain} />
        </View>
      </View>
    )
  }

  return (
    <View className="mt-4 gap-3">
      <Text className="font-sans text-label text-ink-muted">
        {quote.originLabel} → {quote.destinationLabel} · ida {formatDate(quote.outboundDate!)}
        {quote.inboundDate ? ` · vuelta ${formatDate(quote.inboundDate)}` : ''}
      </Text>

      {options.map((option) => (
        <FlightOptionCard
          key={option.quoteId}
          option={option}
          packagePrice={quote.packagePrice}
          revalidation={revalidated[option.quoteId!]}
          revalidating={revalidatingId === option.quoteId}
          onRevalidate={() => onRevalidate(option.quoteId!)}
        />
      ))}

      {quote.testMode ? (
        <Text className="font-sans text-caption text-ink-muted">
          Inventario de prueba del proveedor: los horarios y precios no corresponden a vuelos comerciales
          reales.
        </Text>
      ) : null}
    </View>
  )
}

function FlightOptionCard({
  option,
  packagePrice,
  revalidation,
  revalidating,
  onRevalidate,
}: {
  option: Option
  packagePrice: Quote['packagePrice']
  revalidation?: flightsApi.FlightQuoteRevalidationResponse
  revalidating: boolean
  onRevalidate: () => void
}) {
  const slices = option.slices ?? []
  const price = revalidation?.currentPrice ?? option.flightPrice
  const total = revalidation?.combinedTotal ?? option.combinedTotal

  return (
    <View className="rounded-md border border-border bg-surface p-3">
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
              {last?.arrivingAt ? formatTime(last.arrivingAt) : '—'} ·{' '}
              {slice.stops === 0 ? 'directo' : slice.stops === 1 ? '1 escala' : `${slice.stops} escalas`}
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

      <View className="mt-3 border-t border-border pt-3">
        <View className="flex-row items-baseline justify-between">
          <Text className="font-sans text-label text-ink-muted">Vuelo</Text>
          <Text className="font-ui700 text-heading text-ink">
            {formatCurrency(price?.amount ?? 0, price?.currency ?? 'USD')}
          </Text>
        </View>
        <View className="mt-1 flex-row items-baseline justify-between">
          <Text className="font-sans text-label text-ink-muted">Paquete</Text>
          <Text className="font-sans text-label text-ink">
            {formatCurrency(packagePrice?.amount ?? 0, packagePrice?.currency ?? 'USD')}
          </Text>
        </View>

        {total ? (
          <View className="mt-2 flex-row items-baseline justify-between border-t border-border pt-2">
            <Text className="font-ui600 text-label text-ink">Total por persona</Text>
            <Text className="font-ui700 text-title text-ink">
              {formatCurrency(total.amount ?? 0, total.currency ?? 'USD')}
            </Text>
          </View>
        ) : (
          // Monedas distintas: se muestran los dos importes y NO se inventa una conversión.
          <Text className="mt-2 border-t border-border pt-2 font-sans text-caption text-ink-muted">
            El paquete y el vuelo se cobran en monedas distintas, así que se muestran por separado.
          </Text>
        )}
      </View>

      {revalidation ? <RevalidationNotice revalidation={revalidation} /> : null}

      <View className="mt-3 flex-row items-center justify-between">
        <Text className="font-sans text-caption text-ink-muted">
          {option.expiresAt ? `Precio válido hasta ${formatTime(option.expiresAt)}` : 'Precio sujeto a confirmación'}
        </Text>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Confirmar el precio de este vuelo"
          onPress={onRevalidate}
          disabled={revalidating}
          className={`h-11 flex-row items-center gap-1.5 px-2 ${revalidating ? 'opacity-40' : 'active:opacity-60'}`}
        >
          <Icon icon={RefreshCw} size={14} color={colors.primary} />
          <Text className="font-ui600 text-label text-primary">
            {revalidating ? 'Confirmando…' : 'Confirmar precio'}
          </Text>
        </Pressable>
      </View>
    </View>
  )
}

/** El resultado de revalidar, con el tono que corresponde: confirmar no es lo mismo que avisar un cambio. */
function RevalidationNotice({ revalidation }: { revalidation: flightsApi.FlightQuoteRevalidationResponse }) {
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
          Antes {formatCurrency(revalidation.previousPrice?.amount ?? 0, revalidation.previousPrice?.currency ?? 'USD')} ·
          ahora {formatCurrency(revalidation.currentPrice.amount ?? 0, revalidation.currentPrice.currency ?? 'USD')}
        </Text>
      ) : null}
    </View>
  )
}

/** Hora local del aeropuerto: el backend la manda sin huso justamente para que no se desplace. */
function formatTime(value: string) {
  const time = value.includes('T') ? value.split('T')[1] : value
  return time.slice(0, 5)
}

function formatDuration(minutes: number) {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return hours === 0 ? `${rest} min` : rest === 0 ? `${hours} h` : `${hours} h ${rest} min`
}
