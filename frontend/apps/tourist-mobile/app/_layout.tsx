import '../global.css'
import { QueryClientProvider } from '@tanstack/react-query'
import { Stack } from 'expo-router'
import { StatusBar } from 'expo-status-bar'
import { SafeAreaProvider } from 'react-native-safe-area-context'
import { SessionProvider } from '@/auth/session'
import { SessionQuerySync } from '@/auth/SessionQuerySync'
import { queryClient } from '@/lib/queryClient'

/**
 * Raíz de la app. El catálogo es PÚBLICO, así que acá no hay ningún guard global: las pantallas que sí
 * necesitan sesión (hoy solo el contenido de Perfil) la piden por su cuenta. Login y registro se
 * presentan como modal para no perder el lugar donde estaba la persona navegando.
 */
export default function RootLayout() {
  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <SessionProvider>
          <SessionQuerySync />
          <StatusBar style="dark" />
          <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: '#F6F9FA' } }}>
            <Stack.Screen name="(tabs)" />
            <Stack.Screen name="(auth)" options={{ presentation: 'modal' }} />
          </Stack>
        </SessionProvider>
      </QueryClientProvider>
    </SafeAreaProvider>
  )
}
