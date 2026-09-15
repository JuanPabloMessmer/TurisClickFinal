import { useRouter } from 'expo-router'
import { useRef } from 'react'
import { Text, View } from 'react-native'
import { useSession } from '@/auth/session'
import { Button, Skeleton } from '@/ui'

/**
 * Guard POR PANTALLA para contenido privado del turista (Mis viajes, detalle de reserva, checkout). No es
 * un guard global: las tabs y el catálogo siguen públicos, y sin sesión se muestra una invitación a
 * entrar en vez de redirigir.
 *
 * Distingue "nunca inició sesión" de "la sesión se perdió acá adentro" (logout o refresh fallido): en el
 * segundo caso se avisa que expiró, para que no parezca que la reserva desapareció.
 */
export function RequireTourist({
  children,
  title = 'Iniciá sesión para continuar',
  message = 'Necesitás una cuenta de turista para ver esta información.',
}: {
  children: React.ReactNode
  title?: string
  message?: string
}) {
  const { status, isAuthenticated } = useSession()
  const wasAuthenticated = useRef(false)

  if (isAuthenticated) wasAuthenticated.current = true

  if (status === 'idle' || status === 'loading') {
    return (
      <View className="gap-3 px-5 pt-4" accessibilityLabel="Cargando">
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-24 w-full" />
      </View>
    )
  }

  if (!isAuthenticated) {
    return wasAuthenticated.current ? (
      <SignInPrompt
        title="Tu sesión expiró"
        message="Iniciá sesión de nuevo para continuar. Tus reservas siguen guardadas."
      />
    ) : (
      <SignInPrompt title={title} message={message} />
    )
  }

  return <>{children}</>
}

export function SignInPrompt({ title, message }: { title: string; message: string }) {
  const router = useRouter()

  return (
    <View className="gap-6 px-5 pt-4">
      <View className="rounded-2xl bg-surface p-6" style={{ elevation: 2 }}>
        <Text className="text-xl font-bold text-ink">{title}</Text>
        <Text className="mt-2 text-base leading-6 text-[#5B7285]">{message}</Text>
      </View>
      <View className="gap-3">
        <Button label="Iniciar sesión" onPress={() => router.push('/(auth)/login')} />
        <Button label="Crear cuenta" variant="outline" onPress={() => router.push('/(auth)/register')} />
      </View>
    </View>
  )
}
