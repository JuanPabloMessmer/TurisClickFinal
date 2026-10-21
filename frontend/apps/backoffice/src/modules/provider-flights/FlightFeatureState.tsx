import { Clock, ShieldOff } from 'lucide-react'
import { Card } from '@/components/ui/card'
import { EmptyState } from '@/components/ui/empty-state'
import { flightErrorKind, flightErrorMessage } from './flightErrors'

/**
 * Lo que se muestra en lugar de una pantalla de vuelos cuando no se puede usar por una razón esperada: la
 * funcionalidad todavía no está activa (501) o la empresa no está habilitada (403). Devuelve null si el error es
 * de otro tipo, para que la pantalla lo maneje como un error común.
 */
export function FlightFeatureState({ error }: { error: unknown }) {
  const kind = flightErrorKind(error)
  if (kind === 'other') return null

  return (
    <Card>
      {kind === 'not-authorized' ? (
        <EmptyState icon={ShieldOff} title="Tu empresa no está autorizada para vender vuelos" description={flightErrorMessage(error)} />
      ) : (
        <EmptyState icon={Clock} title="Vuelos de operador: todavía no disponible" description={flightErrorMessage(error)} />
      )}
    </Card>
  )
}
