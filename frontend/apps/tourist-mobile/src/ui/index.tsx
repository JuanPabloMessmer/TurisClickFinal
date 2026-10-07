import { formatCurrency } from '@turisclick/utils'
import { Image } from 'expo-image'
import type { LucideIcon } from 'lucide-react-native'
import { Mountain } from 'lucide-react-native'
import { useState } from 'react'
import {
  ActivityIndicator,
  Pressable,
  Text,
  TextInput,
  View,
  type PressableProps,
  type TextInputProps,
  type ViewStyle,
} from 'react-native'
import { SafeAreaView, type Edge } from 'react-native-safe-area-context'
import { colors } from '@/theme/colors'
import { elevation } from '@/theme/elevation'

/**
 * Piezas visuales de Tourist Mobile (DESIGN.md). No se comparten con el Backoffice a propósito: aquel
 * usa Radix + Tailwind web sobre el DOM, y acá todo son primitivas de React Native. Lo que sí comparten
 * son los tokens: misma marca, distinta densidad.
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

/** Superficie con elevación real en las dos plataformas (ver theme/elevation.ts). */
export function Surface({
  children,
  level = 'raised',
  className = '',
  style,
}: {
  children: React.ReactNode
  level?: keyof typeof elevation
  className?: string
  style?: ViewStyle
}) {
  return (
    <View className={`bg-surface ${className}`} style={[elevation[level], style]}>
      {children}
    </View>
  )
}

/**
 * Ícono vectorial. Los emoji quedaron solo donde son contenido (intereses del onboarding): como
 * control se renderizaban distinto en cada sistema operativo y el `color` del estado activo ni
 * siquiera los afectaba (DESIGN.md §7).
 */
export function Icon({
  icon: Component,
  size = 20,
  color = colors.ink,
}: {
  icon: LucideIcon
  size?: number
  color?: string
}) {
  return <Component size={size} color={color} strokeWidth={2} />
}

type ButtonVariant = 'primary' | 'accent' | 'outline'

const BUTTON_STYLES: Record<ButtonVariant, { container: string; label: string; spinner: string }> = {
  primary: { container: 'bg-primary', label: 'text-white', spinner: '#FFFFFF' },
  accent: { container: 'bg-accent', label: 'text-ink', spinner: colors.ink },
  outline: { container: 'bg-surface border border-border-control', label: 'text-primary', spinner: colors.primary },
}

/** CTA con altura 52 — por encima del mínimo táctil de 44 y cómodo con el pulgar. */
export function Button({
  label,
  variant = 'primary',
  loading = false,
  disabled = false,
  icon,
  ...rest
}: PressableProps & { label: string; variant?: ButtonVariant; loading?: boolean; icon?: LucideIcon }) {
  const styles = BUTTON_STYLES[variant]
  const isInactive = disabled || loading

  return (
    <Pressable
      accessibilityRole="button"
      // Mientras carga, el botón sólo dibuja un spinner: sin este nombre un lector de pantalla anuncia
      // "botón, ocupado" sin decir cuál, justo en el momento en que importa saberlo. Un `accessibilityLabel`
      // de quien lo usa sigue ganando porque `rest` se expande después.
      accessibilityLabel={label}
      accessibilityState={{ disabled: isInactive, busy: loading }}
      disabled={isInactive}
      className={`h-[52px] flex-row items-center justify-center gap-2 rounded-md px-6 ${styles.container} ${
        isInactive ? 'opacity-40' : 'active:opacity-80'
      }`}
      {...rest}
    >
      {loading ? (
        <ActivityIndicator color={styles.spinner} />
      ) : (
        <>
          {icon && <Icon icon={icon} size={18} color={variant === 'primary' ? '#FFFFFF' : colors.primary} />}
          <Text className={`font-ui600 text-base ${styles.label}`}>{label}</Text>
        </>
      )}
    </Pressable>
  )
}

/**
 * Precio con su moneda, formateado como se escribe la plata en Bolivia (`Bs 120,00`) y no como un
 * registro de base de datos (`BOB 120.00`). Usa el helper compartido con el Backoffice.
 *
 * Nunca se suman ni convierten monedas distintas (regla del dominio). Acepta valores ausentes porque
 * el schema generado marca todo opcional; si falta el monto se dice "Consultar precio" en vez de
 * mostrar un 0 que sería mentira.
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
  const className = size === 'lg' ? 'font-ui700 text-title text-ink' : 'font-ui700 text-heading text-ink'

  if (amount == null) return <Text className={className}>Consultar precio</Text>

  return <Text className={className}>{formatCurrency(amount, currency ?? 'BOB')}</Text>
}

export function SectionHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <View className="mb-3 px-4">
      <Text className="font-ui700 text-title text-ink">{title}</Text>
      {subtitle ? <Text className="mt-0.5 font-sans text-label text-ink-muted">{subtitle}</Text> : null}
    </View>
  )
}

/** Placeholder gris con la forma del contenido real, para que la carga no mueva el layout. */
export function Skeleton({ className = '' }: { className?: string }) {
  return <View className={`rounded-md bg-border ${className}`} accessibilityElementsHidden />
}

export function EmptyState({ title, message }: { title: string; message: string }) {
  return (
    <View className="items-center px-8 py-12">
      <Text className="text-center font-ui600 text-heading text-ink">{title}</Text>
      <Text className="mt-2 text-center font-sans text-body text-ink-muted">{message}</Text>
    </View>
  )
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <View className="items-center px-8 py-12">
      <Text className="text-center font-ui600 text-heading text-ink">No pudimos cargar esto</Text>
      <Text className="mt-2 text-center font-sans text-body text-ink-muted">{message}</Text>
      {onRetry ? (
        <View className="mt-5 w-full max-w-[220px]">
          <Button label="Reintentar" variant="outline" onPress={onRetry} />
        </View>
      ) : null}
    </View>
  )
}

/**
 * Imagen de catálogo. Cuando no hay foto —o la que hay no carga— el lugar no se tapa con un emoji:
 * se muestra una superficie mineral con el nombre del destino en la tipografía de display. Un producto
 * sin foto tiene que verse intencional, no roto (DESIGN.md §8).
 */
export function CatalogImage({
  uri,
  className = '',
  fallbackLabel,
}: {
  uri?: string | null
  className?: string
  fallbackLabel?: string | null
}) {
  const [failedUri, setFailedUri] = useState<string | null>(null)

  if (!uri || uri === failedUri) {
    return (
      <View className={`items-center justify-center bg-brand-900 px-4 ${className}`}>
        {fallbackLabel ? (
          <Text numberOfLines={2} className="text-center font-display text-heading text-white/90">
            {fallbackLabel}
          </Text>
        ) : (
          <Icon icon={Mountain} size={28} color="#FFFFFF" />
        )}
      </View>
    )
  }

  return (
    <Image
      source={{ uri }}
      className={className}
      contentFit="cover"
      // La foto entra con un crossfade corto: sin esto, el salto del placeholder a la imagen es lo
      // primero que se nota en una pantalla donde la fotografía es el contenido.
      transition={200}
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
      className={`h-11 justify-center rounded-full px-4 ${selected ? 'bg-primary' : 'border border-border-control bg-surface'}`}
    >
      <Text className={`font-ui500 text-label ${selected ? 'text-white' : 'text-ink'}`}>{label}</Text>
    </View>
  )

  if (!onPress) return content
  return (
    // El nombre va explícito: en el árbol de accesibilidad de la web el texto anidado no siempre llega a
    // nombrar al control, y un lector de pantalla terminaba anunciando "botón" sin decir cuál.
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ selected }}
      onPress={onPress}
      className="active:opacity-70"
    >
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
      <Text className="mb-1.5 font-ui500 text-label text-ink">{label}</Text>
      <TextInput
        accessibilityLabel={label}
        placeholderTextColor={colors.inkMuted}
        className={`h-[52px] rounded-md border px-4 font-sans text-base text-ink ${
          error ? 'border-danger-fg bg-danger-bg' : 'border-border-control bg-surface'
        }`}
        {...rest}
      />
      {error ? <Text className="mt-1.5 font-sans text-label text-danger-fg">{error}</Text> : null}
    </View>
  )
}

/** Aviso inline para el error que devuelve el backend al enviar un formulario. */
export function FormError({ message }: { message?: string | null }) {
  if (!message) return null
  return (
    <View accessible accessibilityRole="alert" className="rounded-md bg-danger-bg p-4">
      <Text className="font-sans text-body text-danger-fg">{message}</Text>
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
    <View className="flex-row rounded-md bg-border p-1">
      {options.map((option) => {
        const selected = option.value === value
        return (
          <Pressable
            key={option.value}
            accessibilityRole="tab"
            accessibilityState={{ selected }}
            onPress={() => onChange(option.value)}
            className={`h-11 flex-1 items-center justify-center rounded-sm ${selected ? 'bg-surface' : ''}`}
            style={selected ? elevation.raised : undefined}
          >
            <Text className={`font-ui600 text-label ${selected ? 'text-primary' : 'text-ink-muted'}`}>
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
      <Text className="font-ui700 text-caption text-ink">{count}</Text>
    </View>
  )
}

/**
 * Estado de un producto o de una fecha. Vocabulario único en toda la app (DESIGN.md §12): el color
 * nunca viaja solo, siempre con texto.
 */
export function StateBadge({ label, tone }: { label: string; tone: 'success' | 'warning' | 'danger' | 'neutral' }) {
  const tones: Record<typeof tone, string> = {
    success: 'bg-success-bg',
    warning: 'bg-warning-bg',
    danger: 'bg-danger-bg',
    neutral: 'bg-border',
  }
  const text: Record<typeof tone, string> = {
    success: 'text-success-fg',
    warning: 'text-warning-fg',
    danger: 'text-danger-fg',
    neutral: 'text-ink-muted',
  }
  return (
    <View className={`self-start rounded-full px-2.5 py-1 ${tones[tone]}`}>
      <Text className={`font-ui600 text-caption ${text[tone]}`}>{label}</Text>
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
      <Text className="font-ui600 text-label text-ink">{label}</Text>
      {hint ? <Text className="mt-0.5 font-sans text-caption text-ink-muted">{hint}</Text> : null}
    </View>
  )
}
