import { formatDate } from '@turisclick/utils'
import { useRouter } from 'expo-router'
import { ArrowLeft, BadgeCheck, CalendarDays } from 'lucide-react-native'
import { Pressable, ScrollView, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { shortDateLabel } from '@/features/catalog/departures'
import { colors } from '@/theme/colors'
import { elevation } from '@/theme/elevation'
import { Button, CatalogImage, Chip, Icon, Price, Skeleton, StateBadge, Surface } from '@/ui'

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
      style={[{ top: insets.top + 8 }, elevation.raised]}
    >
      <Icon icon={ArrowLeft} size={20} color={colors.ink} />
    </Pressable>
  )
}

export function DetailBlock({ title, body }: { title: string; body: string }) {
  return (
    <View className="mt-6">
      <Text className="mb-1.5 font-ui600 text-heading text-ink">{title}</Text>
      <Text className="font-sans text-body text-ink-muted">{body}</Text>
    </View>
  )
}

/**
 * Quién opera el viaje. No es decoración: para publicar, el backend exige que la empresa esté APROBADA
 * por un administrador, así que todo lo que se ve en el catálogo pasó por esa revisión. Es la única
 * señal de confianza verificada que el sistema produce, y hasta ahora no se le mostraba al turista.
 */
export function OperatorNote({ companyName }: { companyName?: string | null }) {
  if (!companyName) return null

  return (
    <Surface className="mt-5 flex-row items-center gap-3 rounded-md border border-border p-3.5" level="flat">
      <Icon icon={BadgeCheck} size={20} color={colors.primary} />
      <View className="flex-1">
        <Text className="font-ui600 text-label text-ink">{companyName}</Text>
        <Text className="mt-0.5 font-sans text-caption text-ink-muted">
          Operador verificado: TurisClick revisa cada empresa antes de dejarla publicar.
        </Text>
      </View>
    </Surface>
  )
}

/** Crédito de la fotografía: trazabilidad del material y señal de que las fotos son reales. */
export function PhotoCredit({ credit }: { credit?: string | null }) {
  if (!credit) return null
  return <Text className="mt-2 font-sans text-caption text-ink-muted">Foto: {credit}</Text>
}

/** Se dice "Sin cupo" en vez de "0 lugares": la fecha existe pero ya no admite reservas. */
export function slotsLabel(availableSlots?: number) {
  if (availableSlots == null) return ''
  if (availableSlots === 0) return 'Sin cupo'
  return `${availableSlots} ${availableSlots === 1 ? 'lugar' : 'lugares'}`
}

/**
 * Fila de una fecha con cupo. `time` solo lo usan las experiencias; los paquetes no tienen horario.
 * El cupo es lo que decide, así que tiene su propio peso visual en vez de ser texto gris al margen.
 */
export function AvailabilityRow({
  date,
  time,
  availableSlots,
}: {
  date?: string
  time?: string | null
  availableSlots?: number
}) {
  const soldOut = availableSlots === 0
  const last = typeof availableSlots === 'number' && availableSlots > 0 && availableSlots <= 3

  return (
    <View className="flex-row items-center justify-between rounded-md border border-border bg-surface px-4 py-3">
      <View className="flex-row items-center gap-2.5">
        <Icon icon={CalendarDays} size={16} color={colors.inkMuted} />
        <Text className="font-ui500 text-body text-ink">
          {date ? shortDateLabel(date) : '—'}
          {time ? <Text className="font-sans text-ink-muted">{`  ${time.slice(0, 5)}`}</Text> : null}
        </Text>
      </View>
      {availableSlots != null ? (
        <StateBadge
          label={slotsLabel(availableSlots)}
          tone={soldOut ? 'neutral' : last ? 'warning' : 'success'}
        />
      ) : null}
    </View>
  )
}

/** Cuántas fechas se muestran en el detalle: el resto se elige en el calendario de reserva. */
export const DETAIL_DATES_PREVIEW = 3

/**
 * Vista previa de las próximas fechas con sus tres estados. Con meses de disponibilidad no se listan todas:
 * se muestran las primeras y cuántas más hay, y la elección completa ocurre en el calendario de reserva.
 * Sin fechas no se inventa nada: se dice que no hay.
 */
export function AvailabilitySection({
  isLoading,
  slots,
  total,
  children,
}: {
  isLoading: boolean
  slots: unknown[]
  /** Total de fechas disponibles cuando `children` es solo una vista previa. */
  total?: number
  children: React.ReactNode
}) {
  const remaining = total != null ? total - Math.min(total, DETAIL_DATES_PREVIEW) : 0

  return (
    <View>
      <Text className="mb-3 mt-8 font-ui700 text-title text-ink">Próximas salidas</Text>
      {isLoading ? (
        <View className="gap-2">
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-12 w-full" />
        </View>
      ) : slots.length === 0 ? (
        <Text className="font-sans text-body text-ink-muted">
          No hay fechas con cupo por el momento. Consultá más adelante.
        </Text>
      ) : (
        <View className="gap-2">
          {children}
          {remaining > 0 ? (
            <Text className="mt-1 font-sans text-label text-ink-muted">
              {`y ${remaining} ${remaining === 1 ? 'fecha más' : 'fechas más'} — elegí la tuya en el calendario.`}
            </Text>
          ) : null}
        </View>
      )}
    </View>
  )
}

/**
 * Barra de precio + CTA de reserva. El CTA lleva a elegir fecha/salida y viajeros; se deshabilita solo si
 * el producto no tiene fechas con cupo (y lo dice). Elegir es público: la sesión se pide al crear la
 * reserva.
 */
export function BookingBar({
  amount,
  currency,
  priceLabel,
  ctaLabel,
  onPress,
  disabled = false,
}: {
  amount?: number | null
  currency?: string | null
  priceLabel: string
  ctaLabel: string
  onPress: () => void
  disabled?: boolean
}) {
  const insets = useSafeAreaInsets()

  return (
    <Surface
      className="absolute inset-x-0 bottom-0 border-t border-border px-4 pt-4"
      level="overlay"
      style={{ paddingBottom: insets.bottom + 16 }}
    >
      <View className="mb-3 flex-row items-end justify-between">
        <Text className="font-sans text-label text-ink-muted">{priceLabel}</Text>
        <Price amount={amount} currency={currency} size="lg" />
      </View>
      <Button label={ctaLabel} onPress={onPress} disabled={disabled} />
    </Surface>
  )
}

export function DetailSkeleton() {
  return (
    <View>
      <Skeleton className="h-72 w-full rounded-none" />
      <View className="gap-3 p-4">
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
      <Text className="mb-3 font-ui700 text-heading text-ink">Fotos</Text>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 12 }}>
        {usable.map((image, index) => (
          <CatalogImage key={image.id ?? index} uri={image.url} className="h-32 w-44 overflow-hidden rounded-md" />
        ))}
      </ScrollView>
    </View>
  )
}
