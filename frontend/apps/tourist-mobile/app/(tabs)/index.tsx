import { useRouter } from 'expo-router'
import { FlatList, Pressable, RefreshControl, ScrollView, Text, View } from 'react-native'
import { CardSkeleton, ExperienceCard, PackageCard } from '@/features/catalog/cards'
import { byPublishedContent, experienceCountLabel } from '@/features/catalog/destinations'
import { useCities, useExperiences, usePackages } from '@/features/catalog/queries'
import { toApiError } from '@/lib/errors'
import { colors } from '@/theme/colors'
import { EmptyState, ErrorState, Screen, SectionHeader } from '@/ui'

/**
 * Home. Todo lo que se muestra está respaldado por datos reales del backend: no hay "populares",
 * "trending" ni "recomendados" porque no existe ninguna métrica que lo sostenga. Sí se puede hablar de
 * novedades, porque ambas búsquedas ordenan por fecha de creación descendente.
 */
export default function HomeScreen() {
  const router = useRouter()
  const cities = useCities()
  const experiences = useExperiences({ pageSize: 6 })
  const packages = usePackages({ pageSize: 6 })

  const refreshing =
    cities.isRefetching || experiences.isRefetching || packages.isRefetching

  const refreshAll = () => {
    void cities.refetch()
    void experiences.refetch()
    void packages.refetch()
  }

  return (
    <Screen>
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
        <View className="px-5 pb-6 pt-2">
          <Text className="text-3xl font-bold text-ink">Descubrí Bolivia</Text>
          <Text className="mt-1.5 text-base text-[#5B7285]">
            Experiencias y paquetes de operadores locales
          </Text>

          <Pressable
            accessibilityRole="search"
            accessibilityLabel="Buscar experiencias y paquetes"
            onPress={() => router.push('/explore')}
            className="mt-6 h-14 flex-row items-center rounded-2xl bg-surface px-4 active:opacity-80"
            style={{ elevation: 2 }}
          >
            <Text className="text-lg">🔍</Text>
            <Text className="ml-3 text-base text-[#5B7285]">¿A dónde querés ir?</Text>
          </Pressable>
        </View>

        <DestinationsRow />

        <View className="mt-9">
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

        <View className="mt-9">
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
    </Screen>
  )
}

/** Atajo a Explorar ya filtrado por destino. Se oculta entero si falla o no hay ciudades. */
function DestinationsRow() {
  const router = useRouter()
  const { data, isPending, error } = useCities()

  if (error || (!isPending && (data?.length ?? 0) === 0)) return null

  return (
    <View className="mt-2">
      <SectionHeader title="Explorá destinos" />
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ paddingHorizontal: 20, gap: 12 }}
      >
        {isPending
          ? [0, 1, 2].map((key) => <View key={key} className="h-24 w-36 rounded-2xl bg-[#E2E8F0]" />)
          : byPublishedContent(data).map((city) => {
              const count = experienceCountLabel(city.publishedExperienceCount)
              return (
                <Pressable
                  key={city.id}
                  accessibilityRole="button"
                  accessibilityLabel={`Explorar ${city.name}`}
                  onPress={() => router.push({ pathname: '/explore', params: { destinationId: city.id } })}
                  className="h-24 w-36 justify-end rounded-2xl bg-primary p-3 active:opacity-80"
                >
                  <Text className="text-sm font-semibold text-white" numberOfLines={2}>
                    {city.name}
                  </Text>
                  {count ? <Text className="mt-0.5 text-xs text-white/75">{count}</Text> : null}
                </Pressable>
              )
            })}
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
        contentContainerStyle={{ paddingHorizontal: 20, gap: 16 }}
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
    return (
      <EmptyState title={emptyTitle} message="Volvé a mirar en unos días: el catálogo se actualiza seguido." />
    )
  }

  return (
    <FlatList
      horizontal
      showsHorizontalScrollIndicator={false}
      data={data}
      keyExtractor={(item) => String(item.id)}
      contentContainerStyle={{ paddingHorizontal: 20, gap: 16 }}
      renderItem={({ item }) => <View className="w-72">{renderItem(item)}</View>}
    />
  )
}
