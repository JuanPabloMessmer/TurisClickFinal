import type { flightsApi } from '@turisclick/api-client'
import { Search } from 'lucide-react-native'
import { useEffect, useState } from 'react'
import { FlatList, Pressable, Text, TextInput, View } from 'react-native'
import { useAirportSearch } from '@/features/flights/api'
import { airportFromResult, type AirportChoice } from '@/features/flights/search/model'
import { SheetModal } from '@/features/flights/search/SheetModal'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { ErrorState, Icon, Skeleton } from '@/ui'

/** Lo que tarda en "asentarse" lo escrito antes de consultar: una consulta por palabra, no por tecla. */
export const AIRPORT_SEARCH_DEBOUNCE_MS = 250

/**
 * Autocompletado de aeropuertos, como en cualquier buscador de vuelos: se escribe una ciudad, un
 * aeropuerto, un país o un código IATA y la lista se acota. Cada fila dice ciudad, país, nombre del
 * aeropuerto y código, que es lo que hace falta para no confundir dos aeropuertos de la misma ciudad.
 *
 * Como destino sólo ofrece Bolivia, y lo dice: si alguien busca "Madrid" como destino, la lista vacía tiene
 * que explicar por qué en vez de parecer un error.
 */
export function AirportPickerModal({
  visible,
  role,
  exclude,
  onSelect,
  onClose,
}: {
  visible: boolean
  role: flightsApi.AirportSearchRole
  /** El aeropuerto ya elegido del otro lado del viaje: no puede ser también este. */
  exclude?: string | null
  onSelect: (airport: AirportChoice) => void
  onClose: () => void
}) {
  const [text, setText] = useState('')
  const [query, setQuery] = useState('')

  // Cada vez que se abre arranca vacío: la búsqueda anterior era para otra cosa.
  const [lastVisible, setLastVisible] = useState(visible)
  if (visible !== lastVisible) {
    setLastVisible(visible)
    if (visible) {
      setText('')
      setQuery('')
    }
  }

  useEffect(() => {
    const timer = setTimeout(() => setQuery(text), AIRPORT_SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [text])

  const airports = useAirportSearch(query, role)
  const results = (airports.data ?? []).filter((airport) => airport.iata !== exclude)
  const isDestination = role === 'destination'

  return (
    <SheetModal visible={visible} title={isDestination ? '¿A dónde vas?' : '¿Desde dónde salís?'} onClose={onClose}>
      <View className="px-5 pt-4">
        <View className="h-12 flex-row items-center gap-2 rounded-md border border-border-control bg-surface px-3">
          <Icon icon={Search} size={18} color={colors.inkMuted} />
          <TextInput
            accessibilityLabel={isDestination ? 'Buscar aeropuerto de destino' : 'Buscar aeropuerto de origen'}
            placeholder="Ciudad, aeropuerto, país o código"
            placeholderTextColor={colors.inkMuted}
            value={text}
            onChangeText={setText}
            autoFocus
            autoCorrect={false}
            autoCapitalize="none"
            returnKeyType="search"
            className="h-12 flex-1 font-sans text-body text-ink"
          />
          {text ? (
            <Pressable accessibilityRole="button" accessibilityLabel="Borrar búsqueda" onPress={() => setText('')} hitSlop={8}>
              <Text className="text-lg text-ink-muted">✕</Text>
            </Pressable>
          ) : null}
        </View>
        {isDestination ? (
          <Text className="mt-2 font-sans text-caption text-ink-muted">Por ahora, los destinos son aeropuertos de Bolivia.</Text>
        ) : null}
        {!query ? (
          <Text className="mt-4 font-ui600 text-caption uppercase tracking-wide text-ink-muted">
            {isDestination ? 'Aeropuertos de Bolivia' : 'Sugeridos'}
          </Text>
        ) : null}
      </View>

      {airports.isPending ? (
        <View className="gap-3 px-5 pt-4">
          <Skeleton className="h-14 w-full" />
          <Skeleton className="h-14 w-full" />
          <Skeleton className="h-14 w-full" />
        </View>
      ) : airports.isError ? (
        <View className="px-5 pt-6">
          <ErrorState message={toApiError(airports.error).message} onRetry={() => void airports.refetch()} />
        </View>
      ) : (
        <FlatList
          data={results}
          keyExtractor={(airport) => airport.iata ?? ''}
          keyboardShouldPersistTaps="handled"
          contentContainerStyle={{ paddingHorizontal: 20, paddingTop: 8, paddingBottom: 32 }}
          ListEmptyComponent={
            <View className="pt-8">
              <Text className="text-center font-ui600 text-body text-ink">No encontramos aeropuertos para “{query}”.</Text>
              <Text className="mt-1 text-center font-sans text-label text-ink-muted">
                {isDestination
                  ? 'Por ahora sólo se puede volar a destinos en Bolivia.'
                  : 'Probá con el nombre de la ciudad o el código de tres letras.'}
              </Text>
            </View>
          }
          renderItem={({ item }) => <AirportRow airport={item} onPress={() => onSelect(airportFromResult(item))} />}
        />
      )}
    </SheetModal>
  )
}

function AirportRow({ airport, onPress }: { airport: flightsApi.AirportSearchResultResponse; onPress: () => void }) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${airport.city} (${airport.iata}), ${airport.name}, ${airport.country}`}
      onPress={onPress}
      className="flex-row items-center gap-3 border-b border-border py-3 active:opacity-60"
    >
      <View className="h-11 w-14 items-center justify-center rounded-md bg-primary/10">
        <Text className="font-ui700 text-label text-primary">{airport.iata}</Text>
      </View>
      <View className="flex-1">
        <Text className="font-ui600 text-body text-ink" numberOfLines={1}>
          {airport.city}, {airport.country}
        </Text>
        <Text className="font-sans text-caption text-ink-muted" numberOfLines={1}>
          {airport.name}
        </Text>
      </View>
    </Pressable>
  )
}
