import '../global.css'
import { Inter_400Regular, Inter_500Medium, Inter_600SemiBold, Inter_700Bold } from '@expo-google-fonts/inter'
import { Newsreader_600SemiBold } from '@expo-google-fonts/newsreader'
import { QueryClientProvider } from '@tanstack/react-query'
import { useFonts } from 'expo-font'
import { Stack } from 'expo-router'
import { StatusBar } from 'expo-status-bar'
import { SafeAreaProvider } from 'react-native-safe-area-context'
import { SessionProvider } from '@/auth/session'
import { SessionQuerySync } from '@/auth/SessionQuerySync'
import { queryClient } from '@/lib/queryClient'
import { colors } from '@/theme/colors'

/**
 * Raíz de la app. El catálogo es PÚBLICO, así que acá no hay ningún guard global: las pantallas que sí
 * necesitan sesión (hoy solo el contenido de Perfil) la piden por su cuenta. Login y registro se
 * presentan como modal para no perder el lugar donde estaba la persona navegando.
 */
export default function RootLayout() {
  // Inter para la UI y Newsreader solo para los títulos sobre fotografía (DESIGN.md §3). La app NO
  // espera a que carguen: se renderiza con la fuente del sistema y las tipografías entran cuando
  // están. Bloquear el arranque por una fuente es peor que un cambio de tipografía al vuelo.
  useFonts({
    Inter_400Regular,
    Inter_500Medium,
    Inter_600SemiBold,
    Inter_700Bold,
    Newsreader_600SemiBold,
  })

  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <SessionProvider>
          <SessionQuerySync />
          <StatusBar style="dark" />
          <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }}>
            <Stack.Screen name="(tabs)" />
            <Stack.Screen name="(auth)" options={{ presentation: 'modal' }} />
            <Stack.Screen name="onboarding" options={{ presentation: 'fullScreenModal' }} />
          </Stack>
        </SessionProvider>
      </QueryClientProvider>
    </SafeAreaProvider>
  )
}
