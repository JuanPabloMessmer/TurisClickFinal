import { Text, View } from 'react-native'
import { FlightSearchScreen } from '@/features/flights/FlightSearchScreen'
import { Screen } from '@/ui'

/**
 * Vuelos. Buscar y comparar no exige cuenta: el backend permite la búsqueda anónima mientras el proveedor
 * sea el simulado. La sesión se pide al empezar una reserva, no antes.
 */
export default function FlightsScreen() {
  return (
    <Screen>
      <View className="px-5 pb-4 pt-2">
        <Text className="text-2xl font-bold text-ink">Vuelos</Text>
      </View>
      <FlightSearchScreen />
    </Screen>
  )
}
