import { formatDate } from '@turisclick/utils'
import { useRouter } from 'expo-router'
import { Pressable, ScrollView, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { Button, CatalogImage, Chip, Price, Skeleton } from '@/ui'

/**
 * Piezas compartidas por los dos detalles (experiencia y paquete). Viven acá y no dentro de un archivo
 * de ruta porque Expo Router solo consume el default export de cada pantalla.
 */

/** Botón de volver flotando sobre la foto: la imagen llega hasta el borde superior de la pantalla. */
export function BackButton() {
  const router = useRouter()
  const insets = useSafeAreaInsets()

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel="Volver"
      onPress={() => router.back()}
      className="absolute left-4 z-10 h-11 w-11 items-center justify-center rounded-full bg-surface/95 active:opacity-70"
      style={{ top: insets.top + 8, elevation: 4 }}
    >
      <Text className="text-lg">←</Text>
    </Pressable>
  )
}

export function DetailBlock({ title, body }: { title: string; body: string }) {
  return (
    <View className="mt-6">
      <Text className="mb-1.5 text-base font-semibold text-ink">{title}</Text>
      <Text className="text-base leading-6 text-[#5B7285]">{body}</Text>
    </View>
  )
}

/** Se dice "Sin cupo" en vez de "0 lugares": la fecha existe pero ya no admite reservas. */
export function slotsLabel(availableSlots?: number) {
  if (availableSlots == null) return ''
  if (availableSlots === 0) return 'Sin cupo'
  return `${availableSlots} ${availableSlots === 1 ? 'lugar' : 'lugares'}`
}

/** Fila de una fecha con cupo. `time` solo lo usan las experiencias; los paquetes no tienen horario. */
export function AvailabilityRow({
  date,
  time,
  availableSlots,
}: {
  date?: string
  time?: string | null
  availableSlots?: number
}) {
  return (
    <View className="flex-row items-center justify-between rounded-xl bg-surface px-4 py-3" style={{ elevation: 1 }}>
      <Text className="text-base text-ink">
        {date ? formatDate(date) : '—'}
        {time ? <Text className="text-[#5B7285]">{`  ${time.slice(0, 5)}`}</Text> : null}
      </Text>
      <Text className="text-sm text-[#5B7285]">{slotsLabel(availableSlots)}</Text>
    </View>
  )
}

/** Lista de fechas con sus tres estados. Sin fechas no se inventa nada: se dice que no hay. */
export function AvailabilitySection({
  isLoading,
  slots,
  children,
}: {
  isLoading: boolean
  slots: unknown[]
  children: React.ReactNode
}) {
  return (
    <View>
      <Text className="mb-3 mt-8 text-lg font-bold text-ink">Fechas disponibles</Text>
      {isLoading ? (
        <View className="gap-2">
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-12 w-full" />
        </View>
      ) : slots.length === 0 ? (
        <Text className="text-sm text-[#5B7285]">
          No hay fechas con cupo por el momento. Consultá más adelante.
        </Text>
      ) : (
        <View className="gap-2">{children}</View>
      )}
    </View>
  )
}

/**
 * Barra de precio + CTA. En Fase 1 el botón está deshabilitado y lo dice explícitamente: prepara el
 * layout definitivo sin simular una función que todavía no existe (reservar llega en Fase 2).
 */
export function BookingBar({
  amount,
  currency,
  priceLabel,
}: {
  amount?: number | null
  currency?: string | null
  priceLabel: string
}) {
  const insets = useSafeAreaInsets()

  return (
    <View
      className="absolute inset-x-0 bottom-0 border-t border-[#E2E8F0] bg-surface px-5 pt-4"
      style={{ paddingBottom: insets.bottom + 16 }}
    >
      <View className="mb-3 flex-row items-baseline justify-between">
        <Text className="text-sm text-[#5B7285]">{priceLabel}</Text>
        <Price amount={amount} currency={currency} size="lg" />
      </View>
      <Button label="Reservas disponibles próximamente" disabled />
    </View>
  )
}

export function DetailSkeleton() {
  return (
    <View>
      <Skeleton className="h-72 w-full rounded-none" />
      <View className="gap-3 p-5">
        <Skeleton className="h-3 w-24" />
        <Skeleton className="h-6 w-3/4" />
        <Skeleton className="h-4 w-40" />
        <Skeleton className="mt-4 h-20 w-full" />
      </View>
    </View>
  )
}

/** Fila de etiquetas sin acción: duración, días, categorías. Se omite entera si no hay ninguna. */
export function TagRow({ tags }: { tags: (string | null | undefined)[] }) {
  const visible = tags.filter((tag): tag is string => Boolean(tag))
  if (visible.length === 0) return null

  return (
    <View className="mt-4 flex-row flex-wrap gap-2">
      {visible.map((tag) => (
        <Chip key={tag} label={tag} />
      ))}
    </View>
  )
}

/**
 * Galería horizontal cuando el producto tiene más de una foto. Con una sola no se muestra: un carrusel
 * de un elemento solo agrega ruido.
 */
export function Gallery({ images }: { images?: { id?: string; url?: string | null }[] | null }) {
  const usable = (images ?? []).filter((image) => Boolean(image.url))
  if (usable.length < 2) return null

  return (
    <View className="mt-6">
      <Text className="mb-3 text-lg font-bold text-ink">Fotos</Text>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 12 }}>
        {usable.map((image, index) => (
          <CatalogImage
            key={image.id ?? index}
            uri={image.url}
            className="h-32 w-44 overflow-hidden rounded-xl"
          />
        ))}
      </ScrollView>
    </View>
  )
}
