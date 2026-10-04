import type { PublicDestinationResponse } from '@turisclick/api-client'
import { useRouter } from 'expo-router'
import { ChevronRight, Search, Sparkles } from 'lucide-react-native'
import { FlatList, Pressable, RefreshControl, ScrollView, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { CardSkeleton, ExperienceCard, PackageCard } from '@/features/catalog/cards'
import { DestinationCard, DestinationCardSkeleton } from '@/features/catalog/DestinationCard'
import { byPublishedContent } from '@/features/catalog/destinations'
import { useCities, useExperiences, usePackages } from '@/features/catalog/queries'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { CatalogImage, EmptyState, ErrorState, Icon, SectionHeader } from '@/ui'
import { Scrim } from '@/ui/Scrim'

/**
 * Home. Todo lo que se muestra está respaldado por datos reales del backend: no hay "populares",
 * "trending" ni "recomendados" porque no existe ninguna métrica que lo sostenga. Sí se puede hablar de
 * novedades, porque ambas búsquedas ordenan por fecha de creación descendente.
 *
 * La fotografía abre la pantalla (DESIGN.md §8): antes el Home arrancaba con un título tipográfico y un
 * buscador, y la primera imagen de Bolivia aparecía recién al tercer bloque.
 */
export default function HomeScreen() {
  const cities = useCities()
  const experiences = useExperiences({ pageSize: 6 })
  const packages = usePackages({ pageSize: 6 })

  const refreshing = cities.isRefetching || experiences.isRefetching || packages.isRefetching

  const refreshAll = () => {
    void cities.refetch()
    void experiences.refetch()
    void packages.refetch()
  }

  const ranked = byPublishedContent(cities.data)

  return (
    <View className="flex-1 bg-background">
      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={{ paddingBottom: 32 }}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={refreshAll}
            tintColor={colors.primary}
            colors={[colors.primary]}
          />
        }
      >
        <Hero destination={ranked[0]} isLoading={cities.isPending} />

        <View className="mt-7">
          <DestinationsRow destinations={ranked} isLoading={cities.isPending} hasError={Boolean(cities.error)} />
        </View>

        <AssistantEntry />

        <View className="mt-8">
          <SectionHeader title="Experiencias nuevas" subtitle="Lo último publicado por los operadores" />
          <HorizontalCatalog
            isLoading={experiences.isPending}
            error={experiences.error}
            onRetry={experiences.refetch}
            isEmpty={experiences.data?.items?.length === 0}
            emptyTitle="Todavía no hay experiencias"
            data={experiences.data?.items ?? []}
            renderItem={(item) => <ExperienceCard experience={item} />}
          />
        </View>

        <View className="mt-8">
          <SectionHeader title="Paquetes nuevos" subtitle="Viajes completos armados por el operador" />
          <HorizontalCatalog
            isLoading={packages.isPending}
            error={packages.error}
            onRetry={packages.refetch}
            isEmpty={packages.data?.items?.length === 0}
            emptyTitle="Todavía no hay paquetes"
            data={packages.data?.items ?? []}
            renderItem={(item) => <PackageCard package={item} />}
          />
        </View>
      </ScrollView>
    </View>
  )
}

/**
 * Apertura: foto del destino con más contenido publicado, velo de marca y el acceso a buscar encima.
 * El destino del hero no se elige a dedo ni por "popularidad" inventada — es aquel para el que
 * TurisClick realmente tiene experiencias publicadas.
 */
function Hero({ destination, isLoading }: { destination?: PublicDestinationResponse; isLoading: boolean }) {
  const router = useRouter()
  const insets = useSafeAreaInsets()

  return (
    <View className="h-[340px] w-full bg-brand-900">
      {!isLoading && (
        <CatalogImage uri={destination?.imageUrl} className="absolute inset-0 h-full w-full" fallbackLabel={null} />
      )}
      {/* Velo de marca: blanco sobre este degradado mide 9.3:1 incluso con una foto clara debajo. */}
      <Scrim height="85%" />

      <View className="flex-1 justify-end px-4 pb-5" style={{ paddingTop: insets.top + 8 }}>
        <Text className="font-display text-display text-white">Descubrí Bolivia</Text>
        <Text className="mt-1.5 font-sans text-body text-white/85">
          Experiencias y paquetes de operadores locales
        </Text>
        {destination?.name ? (
          <Text className="mt-1 font-sans text-caption text-white/70">Foto: {destination.name}</Text>
        ) : null}

        <Pressable
          accessibilityRole="search"
          accessibilityLabel="Buscar experiencias y paquetes"
          onPress={() => router.push('/explore')}
          className="mt-5 h-[52px] flex-row items-center gap-3 rounded-md bg-surface px-4 active:opacity-90"
        >
          <Icon icon={Search} size={18} color={colors.inkMuted} />
          <Text className="font-sans text-base text-ink-muted">¿A dónde querés ir?</Text>
        </Pressable>
      </View>
    </View>
  )
}

/** Acceso al asistente. Sigue siendo una tarjeta, pero deja de competir con la fotografía de arriba. */
function AssistantEntry() {
  const router = useRouter()

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel="Planificá tu viaje con el asistente"
      onPress={() => router.push('/assistant')}
      className="mx-4 mt-8 flex-row items-center gap-3 rounded-lg border border-border bg-surface p-4 active:opacity-90"
    >
      <View className="h-11 w-11 items-center justify-center rounded-full bg-primary">
        <Icon icon={Sparkles} size={20} color="#FFFFFF" />
      </View>
      <View className="flex-1">
        <Text className="font-ui600 text-heading text-ink">Planificá con el asistente</Text>
        <Text className="mt-0.5 font-sans text-label text-ink-muted">
          Contale tu viaje y lo arma con experiencias reales que podés reservar.
        </Text>
      </View>
      <Icon icon={ChevronRight} size={20} color={colors.inkMuted} />
    </Pressable>
  )
}

let shortcutSequence = 0

/**
 * Marca única por toque. Explorar vive montada en su tab y solo aplica un acceso rápido cuando esta marca
 * cambia: sin ella, volver a tocar el mismo destino no tendría efecto si mientras tanto se cambió el
 * filtro desde el panel.
 */
function nextShortcutToken() {
  shortcutSequence += 1
  return `${Date.now()}-${shortcutSequence}`
}

/** Atajo a Explorar ya filtrado por destino. Se oculta entero si falla o no hay ciudades. */
function DestinationsRow({
  destinations,
  isLoading,
  hasError,
}: {
  destinations: PublicDestinationResponse[]
  isLoading: boolean
  hasError: boolean
}) {
  const router = useRouter()

  if (hasError || (!isLoading && destinations.length === 0)) return null

  return (
    <View>
      <SectionHeader title="Explorá destinos" subtitle="Ciudades con experiencias reales para reservar" />
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ paddingHorizontal: 16, gap: 12 }}
      >
        {isLoading
          ? [0, 1, 2].map((key) => <DestinationCardSkeleton key={key} />)
          : destinations.map((city) => (
              <DestinationCard
                key={city.id}
                destination={city}
                onPress={() =>
                  router.push({
                    pathname: '/explore',
                    params: { destinationId: city.id, shortcutAt: nextShortcutToken() },
                  })
                }
              />
            ))}
      </ScrollView>
    </View>
  )
}

/** Carrusel horizontal con sus tres estados: cargando, error y vacío. */
function HorizontalCatalog<T extends { id?: string }>({
  isLoading,
  error,
  onRetry,
  isEmpty,
  emptyTitle,
  data,
  renderItem,
}: {
  isLoading: boolean
  error: unknown
  onRetry: () => void
  isEmpty?: boolean
  emptyTitle: string
  data: T[]
  renderItem: (item: T) => React.ReactElement
}) {
  if (error) return <ErrorState message={toApiError(error).message} onRetry={onRetry} />

  if (isLoading) {
    return (
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ paddingHorizontal: 16, gap: 16 }}
      >
        {[0, 1].map((key) => (
          <View key={key} className="w-72">
            <CardSkeleton />
          </View>
        ))}
      </ScrollView>
    )
  }

  if (isEmpty) {
    return <EmptyState title={emptyTitle} message="Volvé a mirar en unos días: el catálogo se actualiza seguido." />
  }

  return (
    <FlatList
      horizontal
      showsHorizontalScrollIndicator={false}
      data={data}
      keyExtractor={(item) => String(item.id)}
      contentContainerStyle={{ paddingHorizontal: 16, gap: 16 }}
      renderItem={({ item }) => <View className="w-72">{renderItem(item)}</View>}
    />
  )
}
