import type { flightsApi } from '@turisclick/api-client'
import { formatCurrency, formatDate } from '@turisclick/utils'
import { useRouter } from 'expo-router'
import { Plane, Search } from 'lucide-react-native'
import { useMemo, useState } from 'react'
import { Pressable, ScrollView, Text, View } from 'react-native'
import { useAirports, useFlightSearch } from '@/features/flights/api'
import { FlightItinerary } from '@/features/flights/components'
import { toApiError } from '@/lib/errors'
import { Button, Chip, EmptyState, ErrorState, FieldLabel, Icon, Skeleton, Surface } from '@/ui'

/**
 * Explorador de vuelos.
 *
 * Esta pantalla **no vende pasajes**, y está construida para que eso se note. En TurisClick un vuelo se
 * compra dentro de un paquete —la reserva aérea cuelga de una reserva de paquete— así que acá no hay botón
 * de reservar, no hay "seleccionar" y el backend no devuelve ningún identificador reservable. Lo que sí
 * hace es mostrar qué se vuela realmente en esa ruta y llevar a los paquetes que la cubren, que es lo
 * único que se puede comprar.
 *
 * Las ofertas salen del entorno de pruebas del proveedor y se rotulan como tales en la pantalla, no en una
 * nota al pie: un precio de prueba que parezca real es peor que no mostrarlo.
 */

/** Ida y vuelta se pide eligiendo una fecha de regreso; sin ella, es sólo ida. */
type Draft = {
  originIata: string
  destinationIata: string
  departureInDays: number
  passengers: number
}

const EMPTY: Draft = { originIata: '', destinationIata: '', departureInDays: 14, passengers: 1 }

/** Opciones de fecha relativas: un selector de calendario completo es otra pantalla, no este primer paso. */
const DATE_OPTIONS = [
  { days: 7, label: 'En 1 semana' },
  { days: 14, label: 'En 2 semanas' },
  { days: 30, label: 'En 1 mes' },
  { days: 60, label: 'En 2 meses' },
]

function isoInDays(days: number) {
  const date = new Date()
  date.setDate(date.getDate() + days)
  return date.toISOString().slice(0, 10)
}

export function FlightSearchScreen() {
  const router = useRouter()
  const airports = useAirports()
  const search = useFlightSearch()
  const [draft, setDraft] = useState<Draft>(EMPTY)

  // Bolivia primero: es donde opera el catálogo. El backend ya los devuelve en ese orden.
  const options = useMemo(() => airports.data ?? [], [airports.data])

  const ready = Boolean(draft.originIata) && Boolean(draft.destinationIata) && draft.originIata !== draft.destinationIata

  const result = search.data
  const failure = search.error ? toApiError(search.error) : null

  const run = () =>
    search.mutate({
      originIata: draft.originIata,
      destinationIata: draft.destinationIata,
      departureDate: isoInDays(draft.departureInDays),
      passengers: draft.passengers,
      cabinClass: 'ECONOMY',
    })

  return (
    <ScrollView contentContainerStyle={{ paddingBottom: 32 }} keyboardShouldPersistTaps="handled">
      <View className="px-5">
        <Text className="font-sans text-body text-ink-muted">
          Mirá qué vuelos hay para una ruta y con qué paquetes de TurisClick se viaja.
        </Text>

        <View className="mt-5">
          <FieldLabel label="¿Desde dónde salís?" />
          <AirportPicker
            testID="picker-origen"
            airports={options}
            loading={airports.isPending}
            selected={draft.originIata}
            onSelect={(iata) => setDraft((d) => ({ ...d, originIata: iata }))}
          />
        </View>

        <View className="mt-5">
          <FieldLabel label="¿A dónde vas?" />
          <AirportPicker
            testID="picker-destino"
            airports={options}
            loading={airports.isPending}
            selected={draft.destinationIata}
            disabled={draft.originIata}
            onSelect={(iata) => setDraft((d) => ({ ...d, destinationIata: iata }))}
          />
        </View>

        <View className="mt-5">
          <FieldLabel label="¿Cuándo?" />
          <View className="mt-2 flex-row flex-wrap gap-2">
            {DATE_OPTIONS.map((option) => (
              <Chip
                key={option.days}
                label={option.label}
                selected={draft.departureInDays === option.days}
                onPress={() => setDraft((d) => ({ ...d, departureInDays: option.days }))}
              />
            ))}
          </View>
          <Text className="mt-2 font-sans text-caption text-ink-muted">
            {formatDate(isoInDays(draft.departureInDays))}
          </Text>
        </View>

        <View className="mt-5">
          <FieldLabel label="¿Cuántos viajan?" />
          <View className="mt-2 flex-row flex-wrap gap-2">
            {[1, 2, 3, 4].map((count) => (
              <Chip
                key={count}
                label={count === 1 ? '1 persona' : `${count} personas`}
                selected={draft.passengers === count}
                onPress={() => setDraft((d) => ({ ...d, passengers: count }))}
              />
            ))}
          </View>
        </View>

        <View className="mt-6">
          <Button
            label="Buscar vuelos"
            icon={Search}
            loading={search.isPending}
            disabled={!ready}
            onPress={run}
          />
          {!ready && draft.originIata && draft.originIata === draft.destinationIata ? (
            <Text className="mt-2 font-sans text-label text-danger-fg">
              El origen y el destino no pueden ser el mismo aeropuerto.
            </Text>
          ) : null}
        </View>
      </View>

      {search.isPending ? <SearchSkeleton /> : null}

      {failure ? (
        <View className="mt-6 px-5">
          <ErrorState message={failure.message} onRetry={run} />
        </View>
      ) : null}

      {result && !search.isPending ? <Results result={result} onOpenPackage={(id) => router.push(`/package/${id}`)} /> : null}
    </ScrollView>
  )
}

function AirportPicker({
  testID,
  airports,
  loading,
  selected,
  disabled,
  onSelect,
}: {
  /** Los dos selectores muestran los mismos aeropuertos: sin esto no hay forma de distinguirlos. */
  testID: string
  airports: flightsApi.AirportResponse[]
  loading: boolean
  selected: string
  /** El aeropuerto ya elegido como origen: no puede ser también el destino. */
  disabled?: string
  onSelect: (iata: string) => void
}) {
  if (loading) {
    return (
      <View testID={testID} className="mt-2 flex-row gap-2">
        <Skeleton className="h-11 w-32" />
        <Skeleton className="h-11 w-32" />
        <Skeleton className="h-11 w-28" />
      </View>
    )
  }

  return (
    <ScrollView testID={testID} horizontal showsHorizontalScrollIndicator={false} className="mt-2">
      <View className="flex-row gap-2 pr-5">
        {airports
          .filter((airport) => airport.iata !== disabled)
          .map((airport) => (
            <Chip
              key={airport.iata}
              label={airport.label ?? airport.iata ?? ''}
              selected={selected === airport.iata}
              onPress={() => onSelect(airport.iata!)}
            />
          ))}
      </View>
    </ScrollView>
  )
}

function SearchSkeleton() {
  return (
    <View className="mt-6 gap-3 px-5">
      <Skeleton className="h-28 w-full" />
      <Skeleton className="h-28 w-full" />
    </View>
  )
}

function Results({
  result,
  onOpenPackage,
}: {
  result: flightsApi.SearchFlightsResponse
  onOpenPackage: (id: string) => void
}) {
  const options = result.options ?? []
  const packages = result.packages ?? []

  return (
    <View className="mt-7">
      <View className="px-5">
        <Text className="font-display text-title text-ink">
          {result.originLabel} → {result.destinationLabel}
        </Text>
        <Text className="mt-0.5 font-sans text-caption text-ink-muted">
          {result.departureDate ? formatDate(result.departureDate) : ''} ·{' '}
          {result.passengers === 1 ? '1 persona' : `${result.passengers} personas`}
        </Text>
      </View>

      {result.testMode ? (
        <View className="mx-5 mt-3 rounded-md border border-warning-fg/30 bg-warning-bg p-3">
          <Text className="font-ui600 text-label text-warning-fg">Vuelos de prueba</Text>
          <Text className="mt-1 font-sans text-caption text-warning-fg">
            Son ofertas reales de aerolíneas reales, consultadas en el entorno de pruebas del proveedor. Los
            precios sirven para orientarte; no se emite ningún pasaje desde acá.
          </Text>
        </View>
      ) : null}

      {result.notice ? (
        <View className="mt-4 px-5">
          <EmptyState title="Sin vuelos para esa búsqueda" message={result.notice} />
        </View>
      ) : null}

      {options.length > 0 ? (
        <View className="mt-4 gap-3 px-5">
          {options.map((option, index) => (
            <Surface key={index}>
              <View className="p-4">
                <View className="flex-row items-start justify-between gap-3">
                  <View className="flex-1">
                    <View className="flex-row items-center gap-1.5">
                      <Icon icon={Plane} size={14} />
                      <Text className="font-ui600 text-label text-ink">
                        {option.carrierName ?? 'Aerolínea por confirmar'}
                      </Text>
                    </View>
                  </View>
                  <View className="items-end">
                    <Text className="font-display text-heading text-ink">
                      {formatCurrency(option.price?.amount ?? 0, option.price?.currency ?? 'USD')}
                    </Text>
                    <Text className="font-sans text-caption text-ink-muted">
                      {result.passengers === 1 ? 'total' : `total, ${result.passengers} personas`}
                    </Text>
                  </View>
                </View>
                <View className="mt-3 border-t border-border pt-3">
                  <FlightItinerary slices={option.slices ?? []} />
                </View>
              </View>
            </Surface>
          ))}
        </View>
      ) : null}

      <View className="mt-8 px-5">
        <Text className="font-display text-heading text-ink">Cómo se compra un vuelo acá</Text>
        <Text className="mt-1 font-sans text-body text-ink-muted">
          TurisClick no vende pasajes sueltos: el vuelo viaja con el paquete, y el operador se hace cargo de
          los dos. Por eso acá mirás, y reservás en el paquete.
        </Text>

        {packages.length === 0 ? (
          <View className="mt-4">
            <EmptyState
              title="Todavía no hay paquetes en esta ruta"
              message="Ningún operador publicó un paquete con vuelo entre estos dos aeropuertos. Probá con otro destino."
            />
          </View>
        ) : (
          <View className="mt-4 gap-3">
            {packages.map((item) => (
              <Pressable
                key={item.id}
                accessibilityRole="button"
                accessibilityLabel={`Ver ${item.title}`}
                onPress={() => onOpenPackage(item.id!)}
                className="active:opacity-70"
              >
                <Surface>
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
    </View>
  )
}
