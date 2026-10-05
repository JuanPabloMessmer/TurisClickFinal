import { useMutation } from '@tanstack/react-query'
import type { PackageAvailabilityResponse, PackageResponse } from '@turisclick/api-client'
import { flightsApi } from '@turisclick/api-client'
import { formatDate } from '@turisclick/utils'
import { useRouter } from 'expo-router'
import { RefreshCw } from 'lucide-react-native'
import { useState } from 'react'
import { Pressable, Text, View } from 'react-native'
import { useSession } from '@/auth/session'
import {
  FlightItinerary,
  FlightPriceRows,
  FlightSectionHeader,
  RevalidationNotice,
  formatTime,
} from '@/features/flights/components'
import { toApiError } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'
import { colors } from '@/theme/colors'
import { Button, Chip, Icon, Skeleton, Surface } from '@/ui'

type Quote = flightsApi.PackageFlightQuoteResponse
type Option = NonNullable<Quote['options']>[number]

/**
 * El vuelo de un paquete, en la ficha del turista.
 *
 * Regla de producto que esta pantalla hace visible: **el pasaje no tiene precio hasta que se cotiza**.
 * Por eso no se muestra un número hasta que la persona busca, y cuando se muestra, se dice de cuándo es
 * y hasta cuándo vale. Nada de "desde" disfrazado de precio final.
 *
 * Cotizar exige sesión: pedirle inventario a una aerolínea no es lo mismo que leer el catálogo. Sin cuenta
 * se cuenta qué incluye el paquete y desde dónde sale, y se invita a entrar para ver precios.
 */
export function PackageFlightSection({
  pkg,
  availabilities,
}: {
  pkg: PackageResponse
  availabilities: PackageAvailabilityResponse[]
}) {
  // La primera salida con cupo: es la que el turista ve primero en "Próximas salidas", así que es la
  // que se cotiza por defecto.
  const departure = availabilities.find((a) => (a.availableSlots ?? 0) > 0) ?? availabilities[0]

  // El guard va ANTES de cualquier hook, y no es un detalle de estilo: la ficha de un paquete sin vuelo no
  // puede depender de la sesión ni de nada de este módulo. Es pública, y tiene que seguir siéndolo.
  if (!pkg.includesFlight || !departure) return null

  return <FlightQuoting pkg={pkg} departureId={departure.id!} />
}

function FlightQuoting({ pkg, departureId }: { pkg: PackageResponse; departureId: string }) {
  const { isAuthenticated, status: sessionStatus } = useSession()
  const [origin, setOrigin] = useState<string | null>(null)
  const [quote, setQuote] = useState<Quote | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [revalidated, setRevalidated] = useState<Record<string, flightsApi.FlightQuoteRevalidationResponse>>({})

  const search = useMutation({
    mutationFn: (originIata: string) =>
      flightsApi.quotePackageFlights(httpClient, pkg.id!, {
        originIata,
        packageAvailabilityId: departureId,
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

  const origins = pkg.flightOrigins ?? []

  return (
    <View className="mt-8">
      <FlightSectionHeader title="Vuelo incluido" />

      <Surface className="rounded-lg border border-border p-4" level="flat">
        <Text className="font-sans text-body text-ink-muted">
          El pasaje se busca con fechas y precios reales al momento de reservar.
          {pkg.flightDestinationLabel ? ` Volás a ${pkg.flightDestinationLabel}.` : ''}
        </Text>

        {origins.length === 0 ? (
          <Text className="mt-3 font-sans text-label text-ink-muted">
            El operador todavía no publicó desde qué ciudades sale este viaje.
          </Text>
        ) : !isAuthenticated ? (
          <SignInToSeePrices
            origins={origins.map((airport) => airport.label ?? airport.iata ?? '').filter(Boolean)}
            loading={sessionStatus === 'idle' || sessionStatus === 'loading'}
          />
        ) : (
          <>
            <Text className="mt-4 font-ui600 text-label text-ink">¿Desde dónde salís?</Text>
            <View className="mt-2 flex-row flex-wrap gap-2">
              {origins.map((airport) => (
                <Chip
                  key={airport.iata}
                  label={airport.label ?? airport.iata ?? ''}
                  selected={origin === airport.iata}
                  onPress={() => {
                    setOrigin(airport.iata!)
                    setQuote(null)
                    setRevalidated({})
                    search.mutate(airport.iata!)
                  }}
                />
              ))}
            </View>
          </>
        )}

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
 * Sin sesión se dice lo que sí se sabe —desde dónde sale— y se invita a entrar para lo que exige cuenta.
 * No es un muro: es la diferencia entre leer el catálogo y consultarle inventario a una aerolínea.
 */
function SignInToSeePrices({ origins, loading }: { origins: string[]; loading: boolean }) {
  const router = useRouter()

  return (
    <View className="mt-3">
      <Text className="font-sans text-label text-ink">
        Sale desde {origins.length === 1 ? origins[0] : origins.join(' o ')}.
      </Text>
      <Text className="mt-1 font-sans text-label text-ink-muted">
        Iniciá sesión para ver los vuelos disponibles y su precio.
      </Text>
      <View className="mt-3 self-start">
        <Button
          label="Iniciar sesión"
          variant="outline"
          disabled={loading}
          onPress={() => router.push('/(auth)/login')}
        />
      </View>
    </View>
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
  return (
    <View className="rounded-md border border-border bg-surface p-3">
      <FlightItinerary slices={option.slices ?? []} />

      <FlightPriceRows
        flightPrice={revalidation?.currentPrice ?? option.flightPrice}
        packagePrice={packagePrice}
        combinedTotal={revalidation?.combinedTotal ?? option.combinedTotal}
      />

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
