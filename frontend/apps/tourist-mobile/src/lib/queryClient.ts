import { QueryClient, focusManager } from '@tanstack/react-query'
import { AppState, Platform } from 'react-native'
import { toApiError } from './errors'

/**
 * TanStack Query trae detección de foco pensada para el navegador (`visibilitychange`). En React Native
 * el equivalente es `AppState`, y si no se cablea, la librería nunca se entera de que la app volvió del
 * segundo plano: no revalida al volver y, peor, sus reintentos quedan pausados esperando un foco que no
 * llega. Es el cableado que recomienda la propia guía de React Native y no necesita dependencias.
 */
if (Platform.OS !== 'web') {
  focusManager.setEventListener((handleFocus) => {
    const subscription = AppState.addEventListener('change', (state) => handleFocus(state === 'active'))
    return () => subscription.remove()
  })
}

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // El catálogo cambia poco durante una sesión: evita refetchear en cada navegación.
      staleTime: 60_000,

      /**
       * El otro motivo por el que TanStack puede pausar una query es su `onlineManager`, que en React
       * Native tampoco tiene una fuente de conectividad real: habría que cablearlo a NetInfo, que no
       * viene en Expo Go. Una query pausada no falla ni muestra error — se queda en skeleton esperando
       * un evento de reconexión que nunca llega.
       *
       * Con 'always' la request sale igual y un fallo de red se convierte en un error de verdad, que es
       * lo que la app sabe manejar: `toApiError` lo traduce a "No pudimos conectarnos…" y la pantalla
       * ofrece reintentar. Se prefiere un error accionable a una espera infinita.
       */
      networkMode: 'always',

      // En móvil cada reintento cuesta batería y datos; y un 404/403 no mejora reintentando.
      retry: (failureCount, error) => {
        const apiError = toApiError(error)
        if (apiError.isNetworkError) return failureCount < 2
        if (apiError.status && apiError.status < 500) return false
        return failureCount < 1
      },
    },
  },
})
