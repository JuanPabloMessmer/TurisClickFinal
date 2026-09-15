import { Text, View } from 'react-native'
import { RequireTourist } from '@/auth/RequireTourist'
import { TripsList } from '@/features/reservations/TripsList'
import { Screen } from '@/ui'

/**
 * Mis viajes. La TAB es pública —cualquiera puede tocarla—; lo privado es su contenido. Sin sesión se
 * invita a entrar, sin redirigir y sin afectar al resto de las tabs.
 */
export default function TripsScreen() {
  return (
    <Screen>
      <View className="px-5 pb-4 pt-2">
        <Text className="text-2xl font-bold text-ink">Mis viajes</Text>
      </View>
      <RequireTourist
        title="Iniciá sesión para ver tus viajes"
        message="Acá vas a encontrar tus reservas, los pagos pendientes y el estado de cada viaje."
      >
        <TripsList />
      </RequireTourist>
    </Screen>
  )
}
