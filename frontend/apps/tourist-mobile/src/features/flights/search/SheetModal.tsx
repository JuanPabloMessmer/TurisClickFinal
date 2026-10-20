import { Modal, Pressable, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'

/**
 * Panel a pantalla completa del buscador (aeropuerto, fechas, pasajeros, filtros). Mismo patrón que los
 * filtros del catálogo: en un teléfono un selector que ocupa media pantalla deja sin lugar a la lista o al
 * calendario, que es lo que la persona vino a tocar.
 */
export function SheetModal({
  visible,
  title,
  onClose,
  action,
  footer,
  children,
}: {
  visible: boolean
  title: string
  onClose: () => void
  /** Acción secundaria a la derecha del título (p. ej. "Limpiar"). */
  action?: { label: string; onPress: () => void }
  footer?: React.ReactNode
  children: React.ReactNode
}) {
  const insets = useSafeAreaInsets()

  return (
    <Modal visible={visible} animationType="slide" transparent={false} onRequestClose={onClose}>
      <View className="flex-1 bg-background" style={{ paddingTop: insets.top }}>
        <View className="flex-row items-center justify-between border-b border-border px-3 py-2">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Cerrar"
            onPress={onClose}
            className="h-11 w-11 items-center justify-center rounded-full active:opacity-60"
          >
            <Text className="text-xl text-ink">✕</Text>
          </Pressable>
          <Text accessibilityRole="header" className="font-ui700 text-base text-ink">
            {title}
          </Text>
          {action ? (
            <Pressable
              accessibilityRole="button"
              onPress={action.onPress}
              className="h-11 min-w-[44px] items-end justify-center px-2 active:opacity-60"
            >
              <Text className="font-ui600 text-label text-primary">{action.label}</Text>
            </Pressable>
          ) : (
            <View className="w-11" />
          )}
        </View>

        <View className="flex-1">{children}</View>

        {footer ? (
          <View className="border-t border-border bg-surface px-5 pt-3" style={{ paddingBottom: insets.bottom + 12 }}>
            {footer}
          </View>
        ) : null}
      </View>
    </Modal>
  )
}
