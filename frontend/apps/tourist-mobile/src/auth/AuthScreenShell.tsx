
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, Text, View } from 'react-native'
import { useCloseModal } from '@/lib/navigation'
import { Screen } from '@/ui'

/**
 * Marco común de login y registro: cerrar el modal, título, y el teclado empujando el contenido en vez
 * de taparlo (en iOS no lo hace solo).
 */
export function AuthScreenShell({
  title,
  subtitle,
  children,
  footer,
}: {
  title: string
  subtitle: string
  children: React.ReactNode
  footer: React.ReactNode
}) {
  const closeModal = useCloseModal()

  return (
    <Screen edges={['top', 'bottom']}>
      <KeyboardAvoidingView className="flex-1" behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <View className="flex-row px-5 pt-2">
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Cerrar"
            onPress={closeModal}
            className="h-11 w-11 items-center justify-center rounded-full active:opacity-60"
          >
            <Text className="text-xl text-ink">✕</Text>
          </Pressable>
        </View>

        <ScrollView
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
          contentContainerStyle={{ padding: 20, paddingBottom: 40 }}
        >
          <Text className="text-3xl font-bold text-ink">{title}</Text>
          <Text className="mt-1.5 text-base text-[#5B7285]">{subtitle}</Text>

          <View className="mt-8 gap-4">{children}</View>

          <View className="mt-6 items-center">{footer}</View>
        </ScrollView>
      </KeyboardAvoidingView>
    </Screen>
  )
}

/** Enlace de texto para saltar entre login y registro. */
export function AuthSwitchLink({ prompt, action, onPress }: { prompt: string; action: string; onPress: () => void }) {
  return (
    <Pressable accessibilityRole="link" onPress={onPress} className="h-11 justify-center px-2 active:opacity-60">
      <Text className="text-base text-[#5B7285]">
        {prompt} <Text className="font-semibold text-primary">{action}</Text>
      </Text>
    </Pressable>
  )
}
