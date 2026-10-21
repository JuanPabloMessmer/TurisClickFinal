import { Stack, useLocalSearchParams } from 'expo-router'
import { useMemo } from 'react'
import { FlightResultsScreen } from '@/features/flights/FlightResultsScreen'
import { requestFromParams, type SearchParams } from '@/features/flights/search/model'
import { ScreenHeader } from '@/features/reservations/components'
import { Screen } from '@/ui'

/**
 * Resultados del buscador de vuelos. La búsqueda viaja en la URL (origen, destino, fechas, pasajeros y
 * cabina), así que volver atrás y entrar de nuevo muestra lo mismo sin rearmar nada.
 *
 * Sin sesión: mirar resultados no la exige. La sesión se pide al empezar a reservar.
 */
export default function FlightResultsRoute() {
  const params = useLocalSearchParams<SearchParams>()
  const request = useMemo(() => requestFromParams(params), [params])

  return (
    <Screen edges={['top']}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Vuelos" fallback="/flights" />
      <FlightResultsScreen request={request} />
    </Screen>
  )
}
