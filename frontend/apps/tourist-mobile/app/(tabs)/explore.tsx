import { useLocalSearchParams } from 'expo-router'
import { useMemo, useState } from 'react'
import { Pressable, Text, View } from 'react-native'
import { useOnTouristLeave } from '@/auth/useOnTouristLeave'
import { ExperienceCard, PackageCard } from '@/features/catalog/cards'
import { CatalogList } from '@/features/catalog/CatalogList'
import { DestinationBanner } from '@/features/catalog/DestinationCard'
import { FilterSheet } from '@/features/catalog/FilterSheet'
import {
  EMPTY_FILTERS,
  countActiveFilters,
  toExperienceParams,
  toPackageParams,
  type CatalogFilters,
  type CatalogTab,
} from '@/features/catalog/filters'
import { useCities, useInfiniteExperiences, useInfinitePackages } from '@/features/catalog/queries'
import { Badge, Screen, SegmentedControl } from '@/ui'

/**
 * Explorar. Los filtros son exactamente los que aceptan los endpoints públicos; no se ofrece ninguno
 * que el backend no pueda resolver, ni ordenamientos que no existen.
 *
 * Las dos pestañas mantienen su propia query viva: volver de Paquetes a Experiencias no re-descarga
 * nada ni pierde las páginas ya cargadas.
 */
export default function ExploreScreen() {
  // Inicio puede entrar acá con un destino (acceso rápido "Explorá destinos"). `shortcutAt` es una marca
  // única por toque: permite reaplicar el MISMO destino aunque la persona lo haya cambiado desde el panel.
  const params = useLocalSearchParams<{ destinationId?: string; shortcutAt?: string }>()

  const [tab, setTab] = useState<CatalogTab>('experiences')
  const [filters, setFilters] = useState<CatalogFilters>({
    ...EMPTY_FILTERS,
    destinationId: params.destinationId,
  })
  const [filtersOpen, setFiltersOpen] = useState(false)

  // Explorar queda montada en su tab, así que el estado inicial de arriba solo sirve para el primer
  // acceso. Cada toque NUEVO desde Inicio es una búsqueda nueva por ese destino: reemplaza el destino
  // (sigue siendo selección única) y descarta el resto de filtros, para no mezclarla con la búsqueda
  // anterior. Se ajusta durante el render para no disparar una búsqueda con los filtros viejos.
  const shortcutKey = params.destinationId ? `${params.destinationId}|${params.shortcutAt ?? ''}` : null
  const [appliedShortcutKey, setAppliedShortcutKey] = useState(shortcutKey)
  if (shortcutKey !== appliedShortcutKey) {
    setAppliedShortcutKey(shortcutKey)
    if (params.destinationId) {
      setFilters({ ...EMPTY_FILTERS, destinationId: params.destinationId })
      setFiltersOpen(false)
    }
  }

  // Explorar queda montada en su tab entre sesiones: sin esto, los filtros que eligió un turista le
  // quedarían aplicados al siguiente. Solo se resetea este estado local; la caché pública del catálogo
  // no se toca. Mientras la sesión no cambie, los filtros se conservan al navegar.
  useOnTouristLeave(() => {
    setFilters(EMPTY_FILTERS)
    setTab('experiences')
    setFiltersOpen(false)
  })

  const experiences = useInfiniteExperiences(useMemo(() => toExperienceParams(filters), [filters]))
  const packages = useInfinitePackages(useMemo(() => toPackageParams(filters), [filters]))

  const cities = useCities()
  const selectedCity = filters.destinationId ? cities.data?.find((city) => city.id === filters.destinationId) : undefined

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

        {selectedCity ? (
          <View className="mt-4">
            <DestinationBanner destination={selectedCity} onClear={() => setFilters({ ...filters, destinationId: undefined })} />
          </View>
        ) : null}

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
