import { Text, View } from 'react-native'
import { RequireTourist } from '@/auth/RequireTourist'
import { FlightSearchScreen } from '@/features/flights/FlightSearchScreen'
import { Screen } from '@/ui'

/**
 * Vuelos. La TAB es pública —cualquiera puede tocarla—; buscar exige sesión, porque cada búsqueda le pide
 * inventario a un proveedor externo. Mismo criterio que la cotización de un paquete.
 */
export default function FlightsScreen() {
  return (
    <Screen>
      <View className="px-5 pb-4 pt-2">
        <Text className="text-2xl font-bold text-ink">Vuelos</Text>
      </View>
      <RequireTourist
        title="Iniciá sesión para buscar vuelos"
        message="Consultamos disponibilidad real con la aerolínea, así que hace falta una cuenta."
      >
        <FlightSearchScreen />
      </RequireTourist>
    </Screen>
  )
}
