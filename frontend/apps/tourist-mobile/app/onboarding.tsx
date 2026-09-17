import { Stack, useLocalSearchParams, useRouter } from 'expo-router'
import { RequireTourist } from '@/auth/RequireTourist'
import { useSession } from '@/auth/session'
import { OnboardingFlow } from '@/features/preferences/OnboardingFlow'

/**
 * Onboarding de preferencias de viaje. Se abre justo después de crear la cuenta (`mode` ausente) o desde
 * Perfil para editarlas (`mode=edit`). Al terminar vuelve a donde estaba la persona.
 */
export default function OnboardingScreen() {
  const router = useRouter()
  const { mode } = useLocalSearchParams<{ mode?: string }>()
  const { user } = useSession()

  const done = () => (router.canGoBack() ? router.back() : router.replace('/'))

  return (
    <>
      <Stack.Screen options={{ headerShown: false, gestureEnabled: mode === 'edit' }} />
      <RequireTourist>
        <OnboardingFlow mode={mode === 'edit' ? 'edit' : 'onboarding'} firstName={user?.firstName} onDone={done} />
      </RequireTourist>
    </>
  )
}
