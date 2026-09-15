import { Stack, useLocalSearchParams } from 'expo-router'
import { RequireTourist } from '@/auth/RequireTourist'
import { Checkout } from '@/features/reservations/Checkout'
import { ScreenHeader } from '@/features/reservations/components'
import { Screen } from '@/ui'

/** Pagar una reserva pendiente. Contenido privado del turista. */
export default function CheckoutScreen() {
  const { id, quotedPrice, quotedCurrency } = useLocalSearchParams<{
    id: string
    quotedPrice?: string
    quotedCurrency?: string
  }>()

  const quoted = quotedPrice ? Number(quotedPrice) : undefined

  return (
    <Screen edges={['top', 'bottom']}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Confirmá y pagá" />
      <RequireTourist title="Iniciá sesión para pagar tu reserva">
        <Checkout
          id={id}
          quotedPrice={quoted != null && Number.isFinite(quoted) ? quoted : undefined}
          quotedCurrency={quotedCurrency || undefined}
        />
      </RequireTourist>
    </Screen>
  )
}
