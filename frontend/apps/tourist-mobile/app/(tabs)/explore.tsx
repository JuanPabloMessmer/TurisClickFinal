import { useLocalSearchParams } from 'expo-router'
import { useMemo, useState } from 'react'
import { Pressable, Text, View } from 'react-native'
import { ExperienceCard, PackageCard } from '@/features/catalog/cards'
import { CatalogList } from '@/features/catalog/CatalogList'
import { FilterSheet } from '@/features/catalog/FilterSheet'
import {
  EMPTY_FILTERS,
  countActiveFilters,
  toExperienceParams,
  toPackageParams,
  type CatalogFilters,
  type CatalogTab,
} from '@/features/catalog/filters'
import { useInfiniteExperiences, useInfinitePackages } from '@/features/catalog/queries'
import { Badge, Screen, SegmentedControl } from '@/ui'

/**
 * Explorar. Los filtros son exactamente los que aceptan los endpoints públicos; no se ofrece ninguno
 * que el backend no pueda resolver, ni ordenamientos que no existen.
 *
 * Las dos pestañas mantienen su propia query viva: volver de Paquetes a Experiencias no re-descarga
 * nada ni pierde las páginas ya cargadas.
 */
export default function ExploreScreen() {
  // Home puede entrar acá ya filtrado por un destino concreto.
  const params = useLocalSearchParams<{ destinationId?: string }>()

  const [tab, setTab] = useState<CatalogTab>('experiences')
  const [filters, setFilters] = useState<CatalogFilters>({
    ...EMPTY_FILTERS,
    destinationId: params.destinationId,
  })
  const [filtersOpen, setFiltersOpen] = useState(false)

  const experiences = useInfiniteExperiences(useMemo(() => toExperienceParams(filters), [filters]))
  const packages = useInfinitePackages(useMemo(() => toPackageParams(filters), [filters]))

  const activeFilterCount = countActiveFilters(filters, tab)
  const totalCount = (tab === 'experiences' ? experiences : packages).data?.pages[0]?.totalCount
  const emptyMessage =
    activeFilterCount > 0
      ? 'Probá quitando algún filtro o eligiendo otro destino.'
      : 'Todavía no hay nada publicado acá.'

  return (
    <Screen>
      <View className="px-5 pb-4 pt-2">
        <Text className="text-2xl font-bold text-ink">Explorar</Text>

        <View className="mt-4">
          <SegmentedControl
            options={[
              { value: 'experiences', label: 'Experiencias' },
              { value: 'packages', label: 'Paquetes' },
            ]}
            value={tab}
            onChange={setTab}
          />
        </View>

        <View className="mt-4 flex-row items-center justify-between">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={`Filtros${activeFilterCount > 0 ? `, ${activeFilterCount} aplicados` : ''}`}
            onPress={() => setFiltersOpen(true)}
            className="h-11 flex-row items-center rounded-full border border-[#E2E8F0] bg-surface px-4 active:opacity-70"
          >
            <Text className="text-sm font-semibold text-ink">Filtros</Text>
            <Badge count={activeFilterCount} />
          </Pressable>

          {totalCount != null ? (
            <Text className="text-sm text-[#5B7285]">
              {totalCount} {totalCount === 1 ? 'resultado' : 'resultados'}
            </Text>
          ) : null}
        </View>
      </View>

      {tab === 'experiences' ? (
        <CatalogList
          query={experiences}
          emptyTitle="Sin experiencias"
          emptyMessage={emptyMessage}
          renderItem={(item) => <ExperienceCard experience={item} />}
        />
      ) : (
        <CatalogList
          query={packages}
          emptyTitle="Sin paquetes"
          emptyMessage={emptyMessage}
          renderItem={(item) => <PackageCard package={item} />}
        />
      )}

      <FilterSheet
        visible={filtersOpen}
        tab={tab}
        filters={filters}
        onClose={() => setFiltersOpen(false)}
        onApply={(next) => {
          setFilters(next)
          setFiltersOpen(false)
        }}
      />
    </Screen>
  )
}
