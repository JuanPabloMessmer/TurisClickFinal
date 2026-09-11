import { useState } from 'react'
import { Modal, Pressable, ScrollView, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import {
  DURATION_OPTIONS,
  EMPTY_FILTERS,
  PRICE_MAX_OPTIONS,
  type CatalogFilters,
  type CatalogTab,
} from '@/features/catalog/filters'
import { useCategories, useCities } from '@/features/catalog/queries'
import { Button, Chip, FieldLabel } from '@/ui'

/**
 * Panel de filtros a pantalla completa. Se eligió esto y no cinco filas de chips apiladas en Explorar
 * porque en un teléfono esas filas se comen la lista, que es lo que la persona vino a mirar.
 *
 * Edita una copia local y solo la devuelve al presionar "Ver resultados": así se puede tocar varias
 * cosas sin disparar una búsqueda por cada toque.
 */
export function FilterSheet({
  visible,
  tab,
  filters,
  onApply,
  onClose,
}: {
  visible: boolean
  tab: CatalogTab
  filters: CatalogFilters
  onApply: (filters: CatalogFilters) => void
  onClose: () => void
}) {
  const insets = useSafeAreaInsets()
  const [draft, setDraft] = useState(filters)
  const cities = useCities()
  const categories = useCategories()

  // Al reabrir, el borrador arranca de lo que hoy está aplicado.
  const [lastOpened, setLastOpened] = useState(visible)
  if (visible !== lastOpened) {
    setLastOpened(visible)
    if (visible) setDraft(filters)
  }

  /** Tocar la opción ya elegida la quita: es la forma más rápida de deshacer sin un botón por fila. */
  const toggle = <K extends keyof CatalogFilters>(key: K, value: CatalogFilters[K]) =>
    setDraft((current) => ({ ...current, [key]: current[key] === value ? undefined : value }))

  return (
    <Modal visible={visible} animationType="slide" transparent={false} onRequestClose={onClose}>
      <View className="flex-1 bg-background" style={{ paddingTop: insets.top }}>
        <View className="flex-row items-center justify-between border-b border-[#E2E8F0] px-5 py-3">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Cerrar filtros"
            onPress={onClose}
            className="h-11 w-11 items-center justify-center rounded-full active:opacity-60"
          >
            <Text className="text-xl text-ink">✕</Text>
          </Pressable>
          <Text className="text-base font-bold text-ink">Filtros</Text>
          <Pressable
            accessibilityRole="button"
            onPress={() => setDraft(EMPTY_FILTERS)}
            className="h-11 justify-center px-2 active:opacity-60"
          >
            <Text className="text-sm font-semibold text-primary">Limpiar</Text>
          </Pressable>
        </View>

        <ScrollView contentContainerStyle={{ padding: 20, paddingBottom: 32 }} showsVerticalScrollIndicator={false}>
          <FilterGroup label="Destino">
            {(cities.data ?? []).map((city) =>
              city.id && city.name ? (
                <Chip
                  key={city.id}
                  label={city.name}
                  selected={draft.destinationId === city.id}
                  onPress={() => toggle('destinationId', city.id)}
                />
              ) : null,
            )}
          </FilterGroup>

          <FilterGroup label="Categoría">
            {(categories.data ?? []).map((category) =>
              category.id && category.name ? (
                <Chip
                  key={category.id}
                  label={category.name}
                  selected={draft.categoryId === category.id}
                  onPress={() => toggle('categoryId', category.id)}
                />
              ) : null,
            )}
          </FilterGroup>

          <FilterGroup
            label="Precio máximo"
            hint="Se compara contra el monto publicado; las monedas no se convierten entre sí."
          >
            {PRICE_MAX_OPTIONS.map((price) => (
              <Chip
                key={price}
                label={`Hasta ${price}`}
                selected={draft.priceMax === price}
                onPress={() => toggle('priceMax', price)}
              />
            ))}
          </FilterGroup>

          <FilterGroup label="Disponibilidad">
            <Chip
              label="Con cupo desde hoy"
              selected={draft.onlyWithAvailability}
              onPress={() =>
                setDraft((current) => ({ ...current, onlyWithAvailability: !current.onlyWithAvailability }))
              }
            />
          </FilterGroup>

          {/* La duración solo existe para paquetes: `GET /api/experiences` no tiene ese parámetro. */}
          {tab === 'packages' ? (
            <FilterGroup label="Duración">
              {DURATION_OPTIONS.map((option) => {
                const selected = draft.durationDays?.min === option.min && draft.durationDays?.max === option.max
                return (
                  <Chip
                    key={option.label}
                    label={option.label}
                    selected={selected}
                    onPress={() =>
                      setDraft((current) => ({
                        ...current,
                        durationDays: selected ? undefined : { min: option.min, max: option.max },
                      }))
                    }
                  />
                )
              })}
            </FilterGroup>
          ) : null}
        </ScrollView>

        <View
          className="border-t border-[#E2E8F0] bg-surface px-5 pt-4"
          style={{ paddingBottom: insets.bottom + 16 }}
        >
          <Button label="Ver resultados" onPress={() => onApply(draft)} />
        </View>
      </View>
    </Modal>
  )
}

function FilterGroup({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <View className="mb-7">
      <FieldLabel label={label} hint={hint} />
      <View className="flex-row flex-wrap gap-2">{children}</View>
    </View>
  )
}
