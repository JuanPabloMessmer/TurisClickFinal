import type { CompanyResponse, providerFlightsApi } from '@turisclick/api-client'
import { formatDateTime } from '@turisclick/utils'
import { Plane } from 'lucide-react'
import { useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ConfirmActionDialog } from '@/modules/platform/ConfirmActionDialog'
import { useSetFlightSeller } from '@/modules/platform/flightsApi'
import { flightErrorMessage } from '@/modules/provider-flights/flightErrors'

const REGISTRATION_MAX = 100

/**
 * Autorizar a una empresa a vender vuelos propios. Es una habilitación distinta de la aprobación de la empresa:
 * vender pasajes exige un registro o una licencia, y la decisión es del administrador.
 */
export function FlightSellerCard({ company }: { company: CompanyResponse }) {
  const setSeller = useSetFlightSeller(company.id!)
  const [lastResult, setLastResult] = useState<providerFlightsApi.FlightSellerAuthorizationResponse | null>(null)
  const [registration, setRegistration] = useState(company.flightSellerRegistration ?? '')
  const [confirm, setConfirm] = useState<'authorize' | 'revoke' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [validation, setValidation] = useState<string | null>(null)

  const canSell = lastResult?.canSellFlights ?? company.canSellFlights ?? false
  const authorizedAt = lastResult?.authorizedAt ?? company.flightSellerAuthorizedAt
  const currentRegistration = lastResult?.registration ?? company.flightSellerRegistration

  const onConfirm = async () => {
    setError(null)
    try {
      const result = await setSeller.mutateAsync({
        authorized: confirm === 'authorize',
        registration: confirm === 'authorize' ? registration.trim() : (currentRegistration ?? null),
      })
      setLastResult(result)
      setConfirm(null)
    } catch (err) {
      setError(flightErrorMessage(err))
    }
  }

  const openAuthorize = () => {
    setValidation(null)
    if (!registration.trim()) {
      setValidation('Para autorizar la venta de vuelos hay que indicar el registro o la licencia de la empresa.')
      return
    }
    setError(null)
    setConfirm('authorize')
  }

  return (
    <Card className="mb-6">
      <CardContent className="flex flex-col gap-4 pt-6 text-sm">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-2">
            <Plane className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
            <p className="font-medium text-foreground">Venta de vuelos</p>
          </div>
          <Badge variant={canSell ? 'success' : 'neutral'}>{canSell ? 'Autorizada' : 'No autorizada'}</Badge>
        </div>

        {canSell ? (
          <p className="text-muted-foreground">
            Puede publicar vuelos propios
            {currentRegistration ? ` con el registro ${currentRegistration}` : ''}
            {authorizedAt ? `, autorizada el ${formatDateTime(authorizedAt)}` : ''}.
          </p>
        ) : (
          <p className="text-muted-foreground">
            No puede publicar vuelos. Para habilitarla, cargá el registro o la licencia que declaró.
          </p>
        )}

        {!canSell && (
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="flight-seller-registration">Registro o licencia</Label>
            <Input
              id="flight-seller-registration"
              value={registration}
              maxLength={REGISTRATION_MAX}
              onChange={(e) => setRegistration(e.target.value)}
              placeholder="ej. Registro DGAC 0123"
            />
            {validation && <p className="text-xs text-destructive">{validation}</p>}
          </div>
        )}

        <div className="flex flex-wrap gap-2">
          {canSell ? (
            <Button variant="destructive" onClick={() => { setError(null); setConfirm('revoke') }}>
              Revocar autorización
            </Button>
          ) : (
            <Button onClick={openAuthorize}>Autorizar venta de vuelos</Button>
          )}
        </div>
      </CardContent>

      <ConfirmActionDialog
        open={!!confirm}
        title={confirm === 'authorize' ? '¿Autorizar la venta de vuelos?' : '¿Revocar la venta de vuelos?'}
        confirmLabel={confirm === 'authorize' ? 'Autorizar' : 'Revocar'}
        destructive={confirm === 'revoke'}
        pending={setSeller.isPending}
        error={error}
        onConfirm={() => void onConfirm()}
        onClose={() => setConfirm(null)}
      >
        {confirm === 'authorize' ? (
          <p>
            {company.name} va a poder cargar y publicar vuelos propios con el registro «{registration.trim()}». Cada vuelo
            pasa igual por la moderación de TurisClick.
          </p>
        ) : (
          <>
            <p>{company.name} deja de poder cargar, editar y publicar vuelos.</p>
            <p>Lo que ya vendió no se cancela: va a poder seguir registrando la emisión de esos pasajes.</p>
          </>
        )}
      </ConfirmActionDialog>
    </Card>
  )
}
