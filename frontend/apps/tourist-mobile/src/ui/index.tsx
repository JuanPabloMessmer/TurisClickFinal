import { useState } from 'react'
import {
  ActivityIndicator,
  Image,
  Pressable,
  Text,
  TextInput,
  View,
  type PressableProps,
  type TextInputProps,
} from 'react-native'
import { SafeAreaView, type Edge } from 'react-native-safe-area-context'
import { colors } from '@/theme/colors'

/**
 * Piezas visuales de Tourist Mobile. No se comparten con el Backoffice a propósito: aquel usa Radix +
 * Tailwind web sobre el DOM, y acá todo son primitivas de React Native. Lo único que ambos comparten
 * es la paleta.
 */

/** Contenedor de pantalla: fondo de la marca y safe areas respetadas. */
export function Screen({
  children,
  edges = ['top'],
}: {
  children: React.ReactNode
  edges?: readonly Edge[]
}) {
  return (
    <SafeAreaView edges={edges} className="flex-1 bg-background">
      {children}
    </SafeAreaView>
  )
}

type ButtonVariant = 'primary' | 'accent' | 'outline'

const BUTTON_STYLES: Record<ButtonVariant, { container: string; label: string }> = {
  primary: { container: 'bg-primary', label: 'text-white' },
  accent: { container: 'bg-accent', label: 'text-ink' },
  outline: { container: 'bg-transparent border border-primary', label: 'text-primary' },
}

/** CTA con altura 52 — por encima del mínimo táctil de 44 y cómodo con el pulgar. */
export function Button({
  label,
  variant = 'primary',
  loading = false,
  disabled = false,
  ...rest
}: PressableProps & { label: string; variant?: ButtonVariant; loading?: boolean }) {
  const styles = BUTTON_STYLES[variant]
  const isInactive = disabled || loading

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: isInactive, busy: loading }}
      disabled={isInactive}
      className={`h-[52px] flex-row items-center justify-center rounded-2xl px-6 ${styles.container} ${
        isInactive ? 'opacity-40' : 'active:opacity-80'
      }`}
      {...rest}
    >
      {loading ? (
        <ActivityIndicator color={variant === 'primary' ? '#FFFFFF' : colors.primary} />
      ) : (
        <Text className={`text-base font-semibold ${styles.label}`}>{label}</Text>
      )}
    </Pressable>
  )
}

/**
 * Precio con su moneda: nunca se suman ni convierten monedas distintas (regla del dominio).
 * Acepta valores ausentes porque el schema generado marca todo opcional; si falta el monto se dice
 * "Consultar precio" en vez de mostrar un 0 que sería mentira.
 */
export function Price({
  amount,
  currency,
  size = 'md',
}: {
  amount?: number | null
  currency?: string | null
  size?: 'md' | 'lg'
}) {
  const className = size === 'lg' ? 'text-2xl font-bold text-primary' : 'text-base font-bold text-primary'

  if (amount == null) return <Text className={className}>Consultar precio</Text>

  return (
    <Text className={className}>
      {currency ?? ''} {amount.toFixed(2)}
    </Text>
  )
}

export function SectionHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <View className="mb-3 px-5">
      <Text className="text-xl font-bold text-ink">{title}</Text>
      {subtitle ? <Text className="mt-0.5 text-sm text-[#5B7285]">{subtitle}</Text> : null}
    </View>
  )
}

/** Placeholder gris con la forma del contenido real, para que la carga no mueva el layout. */
export function Skeleton({ className = '' }: { className?: string }) {
  return <View className={`rounded-xl bg-[#E2E8F0] ${className}`} accessibilityElementsHidden />
}

export function EmptyState({ title, message }: { title: string; message: string }) {
  return (
    <View className="items-center px-8 py-12">
      <Text className="text-center text-lg font-semibold text-ink">{title}</Text>
      <Text className="mt-2 text-center text-sm text-[#5B7285]">{message}</Text>
    </View>
  )
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <View className="items-center px-8 py-12">
      <Text className="text-center text-lg font-semibold text-ink">No pudimos cargar esto</Text>
      <Text className="mt-2 text-center text-sm text-[#5B7285]">{message}</Text>
      {onRetry ? (
        <View className="mt-5 w-full max-w-[220px]">
          <Button label="Reintentar" variant="outline" onPress={onRetry} />
        </View>
      ) : null}
    </View>
  )
}

/**
 * Imagen de catálogo. Muchos productos todavía no tienen foto cargada, así que en vez de un ícono roto
 * se muestra un bloque en color de marca — se ve intencional y mantiene la altura del card.
 *
 * El mismo placeholder cubre el caso de una URL que existe pero no carga (dominio caído, foto borrada
 * del hosting del operador): sin esto queda un hueco en blanco del alto del card, que se ve como un
 * error de la app y no como una foto faltante.
 */
export function CatalogImage({ uri, className = '' }: { uri?: string | null; className?: string }) {
  const [failedUri, setFailedUri] = useState<string | null>(null)

  if (!uri || uri === failedUri) {
    return (
      <View className={`items-center justify-center bg-secondary/15 ${className}`}>
        <Text className="text-3xl">🏔️</Text>
      </View>
    )
  }

  return (
    <Image
      source={{ uri }}
      className={className}
      resizeMode="cover"
      // Se guarda la URL que falló, no un booleano: así el placeholder no queda pegado si el producto
      // cambia de foto y la nueva sí carga.
      onError={() => setFailedUri(uri)}
    />
  )
}

/** Etiqueta compacta: destino, duración, categoría. */
export function Chip({ label, selected = false, onPress }: { label: string; selected?: boolean; onPress?: () => void }) {
  const content = (
    <View
      className={`h-11 justify-center rounded-full px-4 ${selected ? 'bg-primary' : 'bg-surface border border-[#E2E8F0]'}`}
    >
      <Text className={`text-sm font-medium ${selected ? 'text-white' : 'text-ink'}`}>{label}</Text>
    </View>
  )

  if (!onPress) return content
  return (
    <Pressable accessibilityRole="button" accessibilityState={{ selected }} onPress={onPress} className="active:opacity-70">
      {content}
    </Pressable>
  )
}

/**
 * Campo de texto con label y error. El borde rojo no viaja solo: siempre lo acompaña el mensaje, para
 * que también se entienda sin distinguir colores.
 */
export function TextField({
  label,
  error,
  ...rest
}: TextInputProps & { label: string; error?: string }) {
  return (
    <View>
      <Text className="mb-1.5 text-sm font-medium text-ink">{label}</Text>
      <TextInput
        accessibilityLabel={label}
        placeholderTextColor={colors.inkMuted}
        className={`h-[52px] rounded-2xl border px-4 text-base text-ink ${
          error ? 'border-[#DC2626] bg-[#FEF2F2]' : 'border-[#E2E8F0] bg-surface'
        }`}
        {...rest}
      />
      {error ? <Text className="mt-1.5 text-sm text-[#DC2626]">{error}</Text> : null}
    </View>
  )
}

/** Aviso inline para el error que devuelve el backend al enviar un formulario. */
export function FormError({ message }: { message?: string | null }) {
  if (!message) return null
  return (
    <View accessible accessibilityRole="alert" className="rounded-2xl border border-[#FECACA] bg-[#FEF2F2] p-4">
      <Text className="text-sm text-[#B91C1C]">{message}</Text>
    </View>
  )
}

/**
 * Selector de dos o tres opciones excluyentes. Se prefiere a una fila de chips cuando la elección
 * cambia por completo lo que se lista: el fondo deslizante deja claro que es una sola decisión.
 */
export function SegmentedControl<T extends string>({
  options,
  value,
  onChange,
}: {
  options: { value: T; label: string }[]
  value: T
  onChange: (value: T) => void
}) {
  return (
    <View className="flex-row rounded-2xl bg-[#E8EEF2] p-1">
      {options.map((option) => {
        const selected = option.value === value
        return (
          <Pressable
            key={option.value}
            accessibilityRole="tab"
            accessibilityState={{ selected }}
            onPress={() => onChange(option.value)}
            className={`h-11 flex-1 items-center justify-center rounded-xl ${selected ? 'bg-surface' : ''}`}
            style={selected ? { elevation: 2 } : undefined}
          >
            <Text className={`text-sm font-semibold ${selected ? 'text-primary' : 'text-[#5B7285]'}`}>
              {option.label}
            </Text>
          </Pressable>
        )
      })}
    </View>
  )
}

/** Contador redondo sobre un botón (ej. cuántos filtros hay puestos). */
export function Badge({ count }: { count: number }) {
  if (count <= 0) return null
  return (
    <View className="ml-2 h-5 min-w-[20px] items-center justify-center rounded-full bg-accent px-1.5">
      <Text className="text-xs font-bold text-ink">{count}</Text>
    </View>
  )
}

/** Spinner al pie de una lista paginada, mientras entra la página siguiente. */
export function ListFooterLoader({ visible }: { visible: boolean }) {
  if (!visible) return null
  return (
    <View className="items-center py-6">
      <ActivityIndicator color={colors.primary} />
    </View>
  )
}

/** Título de un grupo de opciones dentro del panel de filtros. */
export function FieldLabel({ label, hint }: { label: string; hint?: string }) {
  return (
    <View className="mb-2.5">
      <Text className="text-sm font-semibold text-ink">{label}</Text>
      {hint ? <Text className="mt-0.5 text-xs text-[#5B7285]">{hint}</Text> : null}
    </View>
  )
}
