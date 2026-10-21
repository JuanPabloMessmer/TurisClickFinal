import { Stack, useLocalSearchParams } from 'expo-router'
import { useMemo } from 'react'
import { departureIdsFromParam, type FlightBookingParams } from '@/features/flights/booking/model'
import { FlightBookingScreen } from '@/features/flights/booking/FlightBookingScreen'
import { requestFromParams } from '@/features/flights/search/model'
import { ScreenHeader } from '@/features/reservations/components'
import { Screen } from '@/ui'

/**
 * Reservar un vuelo de operador. La búsqueda y las salidas elegidas viajan en la URL: así iniciar sesión en
 * el medio (un modal) devuelve a la persona exactamente a este vuelo.
 *
 * La pantalla no se envuelve en RequireTourist: el vuelo se puede revisar sin sesión, y la sesión se pide
 * al empezar a cargar pasajeros.
 */
export default function BookFlightRoute() {
  const params = useLocalSearchParams<FlightBookingParams>()
  const request = useMemo(() => requestFromParams(params), [params])
  const departureIds = useMemo(
    () => (request ? departureIdsFromParam(params.ids, Boolean(request.returnDate)) : null),
    [params.ids, request],
  )

  return (
    <Screen edges={['top']}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Reservar vuelo" fallback="/flights" />
      <FlightBookingScreen request={request} departureIds={departureIds} />
    </Screen>
  )
}
