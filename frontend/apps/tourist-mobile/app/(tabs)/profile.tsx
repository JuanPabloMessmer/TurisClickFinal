import { useRouter } from 'expo-router'
import { Pressable, ScrollView, Text, View } from 'react-native'
import { useSession } from '@/auth/session'
import { useMyPreferences } from '@/features/preferences/api'
import { isEmptyProfile, preferenceSummary } from '@/features/preferences/model'
import { API_CONFIG, API_TARGET_LABEL, APP_VERSION } from '@/lib/env'
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

        <Text className="mt-10 text-center text-xs text-[#5B7285]">
          TurisClick · versión {APP_VERSION} · servidor {API_TARGET_LABEL[API_CONFIG.target]}
        </Text>
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
      <TravelProfileCard />

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

/**
 * Perfil de viaje (onboarding). Es lo único editable del perfil: los datos de la cuenta siguen siendo de
 * solo lectura porque no hay endpoint para cambiarlos.
 */
function TravelProfileCard() {
  const router = useRouter()
  const { data, isPending, isError, refetch } = useMyPreferences()

  if (isPending) return <View className="h-32 rounded-2xl bg-[#E2E8F0]" accessibilityLabel="Cargando preferencias" />

  if (isError) {
    return (
      <Pressable accessibilityRole="button" onPress={() => void refetch()} className="rounded-2xl bg-surface p-5 active:opacity-80">
        <Text className="text-base text-[#5B7285]">No pudimos cargar tus preferencias. Tocá para reintentar.</Text>
      </Pressable>
    )
  }

  const summary = preferenceSummary(data)
  const empty = isEmptyProfile(data)
  const goEdit = () => router.push({ pathname: '/onboarding', params: { mode: 'edit' } })

  return (
    <View className="rounded-2xl bg-surface p-5" style={{ elevation: 2 }}>
      <Text className="text-lg font-bold text-ink">Tu perfil de viaje</Text>
      {empty ? (
        <Text className="mt-2 text-base leading-6 text-[#5B7285]">
          Contanos qué te gusta y el asistente te recomienda mejor, sin que tengas que repetirlo en cada viaje.
        </Text>
      ) : (
        <View className="mt-3 gap-2">
          {summary.interests.length > 0 ? (
            <View className="flex-row flex-wrap gap-2">
              {summary.interests.map((name) => (
                <View key={name} className="rounded-full bg-primary/10 px-3 py-1">
                  <Text className="text-sm font-medium text-primary">{name}</Text>
                </View>
              ))}
            </View>
          ) : null}
          <Text className="text-sm text-[#5B7285]">
            {[summary.pace && `Ritmo ${summary.pace.toLowerCase()}`, summary.party, summary.budget && `Presupuesto ${summary.budget.toLowerCase()}`]
              .filter(Boolean)
              .join(' · ')}
          </Text>
        </View>
      )}
      <View className="mt-4">
        <Button label={empty ? 'Completar mis preferencias' : 'Ajustar preferencias'} variant="outline" onPress={goEdit} />
      </View>
    </View>
  )
}
