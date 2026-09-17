import type { PublicDestinationResponse } from '@turisclick/api-client'
import { Pressable, Text, View } from 'react-native'
import { experienceCountLabel } from '@/features/catalog/destinations'
import { CatalogImage } from '@/ui'

/**
 * Tarjeta visual de un destino: foto representativa a sangre con el nombre encima. Si el destino no tiene
 * imagen (o no carga) CatalogImage muestra el placeholder, y el velo oscuro mantiene el texto legible en
 * los dos casos, así el layout nunca cambia.
 */
export function DestinationCard({
  destination,
  onPress,
  size = 'md',
}: {
  destination: PublicDestinationResponse
  onPress: () => void
  size?: 'md' | 'lg'
}) {
  const count = experienceCountLabel(destination.publishedExperienceCount)
  const dimensions = size === 'lg' ? 'h-56 w-44' : 'h-44 w-36'

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`Explorar ${destination.name}${count ? `, ${count}` : ''}`}
      onPress={onPress}
      className={`${dimensions} overflow-hidden rounded-2xl bg-[#E2E8F0] active:opacity-90`}
      style={{ elevation: 3 }}
    >
      <CatalogImage uri={destination.imageUrl} className="absolute inset-0 h-full w-full" />
      <View className="absolute inset-x-0 bottom-0 h-24 bg-black/40" />
      <View className="absolute inset-x-0 bottom-0 p-3">
        <Text className="text-base font-bold text-white" numberOfLines={2}>
          {destination.name}
        </Text>
        {count ? <Text className="mt-0.5 text-xs font-medium text-white/85">{count}</Text> : null}
      </View>
    </Pressable>
  )
}

export function DestinationCardSkeleton() {
  return <View className="h-44 w-36 rounded-2xl bg-[#E2E8F0]" />
}

/**
 * Encabezado de Explorar cuando hay un destino filtrado: la foto da contexto de dónde se está buscando y
 * ofrece quitar el filtro sin abrir el panel.
 */
export function DestinationBanner({
  destination,
  onClear,
}: {
  destination: PublicDestinationResponse
  onClear: () => void
}) {
  const count = experienceCountLabel(destination.publishedExperienceCount)

  return (
    <View className="h-28 overflow-hidden rounded-2xl bg-[#E2E8F0]" style={{ elevation: 2 }}>
      <CatalogImage uri={destination.imageUrl} className="absolute inset-0 h-full w-full" />
      <View className="absolute inset-0 bg-black/35" />
      <View className="flex-1 flex-row items-end justify-between p-4">
        <View className="flex-1 pr-3">
          <Text className="text-xs font-semibold uppercase tracking-wide text-white/85">Destino</Text>
          <Text className="text-xl font-bold text-white" numberOfLines={1}>
            {destination.name}
          </Text>
          {count ? <Text className="text-xs text-white/85">{count}</Text> : null}
        </View>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={`Quitar el filtro ${destination.name}`}
          onPress={onClear}
          className="h-9 flex-row items-center rounded-full bg-white/90 px-3 active:opacity-70"
        >
          <Text className="text-sm font-semibold text-ink">Quitar ✕</Text>
        </Pressable>
      </View>
    </View>
  )
}
