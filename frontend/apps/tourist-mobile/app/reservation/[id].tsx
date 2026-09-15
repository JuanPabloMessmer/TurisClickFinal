import { Stack, useLocalSearchParams } from 'expo-router'
import { RequireTourist } from '@/auth/RequireTourist'
import { ScreenHeader } from '@/features/reservations/components'
import { ReservationDetail } from '@/features/reservations/ReservationDetail'
import { Screen } from '@/ui'

/** Detalle de una reserva propia. Contenido privado: sin sesión se invita a entrar. */
export default function ReservationDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()

  return (
    <Screen edges={['top', 'bottom']}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Tu reserva" />
      <RequireTourist title="Iniciá sesión para ver tu reserva">
        <ReservationDetail id={id} />
      </RequireTourist>
    </Screen>
  )
}
