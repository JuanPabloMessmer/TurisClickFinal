import { useRouter } from 'expo-router'
import { ScrollView, Text, View } from 'react-native'
import { useSession } from '@/auth/session'
import { APP_VERSION } from '@/lib/env'
import { Button, Screen } from '@/ui'

/**
 * Perfil. No bloquea la app: sin sesión muestra el CTA para entrar o registrarse, porque todo el
 * catálogo se puede navegar sin cuenta y esta es la única puerta a lo privado en Fase 1.
 */
export default function ProfileScreen() {
  const { status, isAuthenticated } = useSession()

  return (
    <Screen>
      <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ padding: 20, paddingBottom: 40 }}>
        <Text className="text-2xl font-bold text-ink">Perfil</Text>

        <View className="mt-6">
          {status === 'idle' || status === 'loading' ? (
            <View className="h-40 rounded-2xl bg-[#E2E8F0]" accessibilityElementsHidden />
          ) : isAuthenticated ? (
            <SignedIn />
          ) : (
            <SignedOut />
          )}
        </View>

        <Text className="mt-10 text-center text-xs text-[#5B7285]">TurisClick · versión {APP_VERSION}</Text>
      </ScrollView>
    </Screen>
  )
}

function SignedIn() {
  const router = useRouter()
  const { user, logout } = useSession()
  const initial = (user?.firstName ?? user?.email ?? '?').charAt(0).toUpperCase()

  return (
    <View className="gap-6">
      <View className="items-center rounded-2xl bg-surface p-6" style={{ elevation: 2 }}>
        <View className="h-20 w-20 items-center justify-center rounded-full bg-primary">
          <Text className="text-3xl font-bold text-white">{initial}</Text>
        </View>
        <Text className="mt-4 text-xl font-bold text-ink">{user?.fullName ?? 'Turista'}</Text>
        {user?.email ? <Text className="mt-1 text-sm text-[#5B7285]">{user.email}</Text> : null}
      </View>

      {/*
        Los datos de la cuenta son de solo lectura: el backend todavía no expone un endpoint de edición de
        perfil, así que ofrecer un formulario sería prometer algo que no existe.
      */}
      <Button label="Ver mis viajes" onPress={() => router.push('/trips')} />

      <Button label="Cerrar sesión" variant="outline" onPress={() => void logout()} />
    </View>
  )
}

function SignedOut() {
  const router = useRouter()

  return (
    <View className="gap-6">
      <View className="rounded-2xl bg-surface p-6" style={{ elevation: 2 }}>
        <Text className="text-xl font-bold text-ink">Todavía no iniciaste sesión</Text>
        <Text className="mt-2 text-base leading-6 text-[#5B7285]">
          Podés seguir explorando experiencias y paquetes sin cuenta. Creá una para guardar tus datos y
          reservar.
        </Text>
      </View>

      <View className="gap-3">
        <Button label="Iniciar sesión" onPress={() => router.push('/(auth)/login')} />
        <Button label="Crear cuenta" variant="outline" onPress={() => router.push('/(auth)/register')} />
      </View>
    </View>
  )
}
