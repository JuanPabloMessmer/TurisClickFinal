import type { adminApi } from '@turisclick/api-client'
import { RefreshCw } from 'lucide-react'
import { useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import { getErrorMessage } from '@/lib/errors'
import { useResolveCancellations, useUnresolvedCancellations } from './api'
import { CANCELLATION_LABEL } from './AdminReservationsPage'

/**
 * UC-A-10 — las cancelaciones que quedaron a medias y la forma de destrabarlas.
 *
 * El proceso de fondo las reintenta solo, pero vive dentro de la API: en el plan gratuito de Azure la app se
 * duerme sin tráfico y mientras duerme no reintenta nada. "Reintentar ahora" corre la misma resolución en el
 * momento. Es idempotente del lado del servidor, así que un segundo clic no devuelve la plata dos veces; igual
 * el botón se bloquea mientras viaja.
 */
export function UnresolvedCancellationsPanel() {
  const cancellations = useUnresolvedCancellations()
  const resolve = useResolveCancellations()
  const [result, setResult] = useState<adminApi.ResolvePendingCancellationsResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const inFlight = useRef(false)

  const items = cancellations.data ?? []

  const retry = () => {
    // `isPending` recién cambia cuando la mutación arranca, que es asíncrono: un doble clic llega antes. La
    // referencia corta el segundo envío en el acto; el botón bloqueado es lo que se ve.
    if (inFlight.current) return
    inFlight.current = true
    setError(null)
    setResult(null)
    resolve.mutate(undefined, {
      onSettled: () => {
        inFlight.current = false
      },
      onSuccess: setResult,
      onError: (mutationError) => setError(getErrorMessage(mutationError)),
    })
  }

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="text-body font-semibold text-foreground">Cancelaciones a medias</h2>
            <p className="text-label text-ink-muted">
              Reembolsos pendientes y casos en revisión. Se reintentan solos, pero si la app estuvo dormida podés
              reintentarlos ahora.
            </p>
          </div>
          <Button
            variant="outline"
            onClick={retry}
            loading={resolve.isPending}
            disabled={cancellations.isPending || items.length === 0}
          >
            {!resolve.isPending && <RefreshCw className="h-4 w-4" aria-hidden="true" />}
            Reintentar ahora
          </Button>
        </div>

        {result && (
          <Alert variant={result.stillPending ? 'warning' : 'success'}>
            {result.stillPending
              ? `Se resolvieron ${result.completed ?? 0} de ${result.attempted ?? 0}. Quedan ${result.stillPending} pendientes: revisá el detalle de cada reserva.`
              : `Se resolvieron ${result.completed ?? 0} de ${result.attempted ?? 0}. No queda ninguna pendiente.`}
          </Alert>
        )}
        {error && <Alert variant="destructive">{error}</Alert>}

        {cancellations.isPending ? (
          <div className="flex justify-center py-4">
            <Spinner />
          </div>
        ) : cancellations.isError ? (
          <Alert variant="destructive">{getErrorMessage(cancellations.error)}</Alert>
        ) : items.length === 0 ? (
          <p className="text-label text-ink-muted">Ninguna cancelación quedó a medias.</p>
        ) : (
          <ul className="divide-y divide-border">
            {items.map((cancellation) => (
              <li key={cancellation.id} className="flex flex-wrap items-center justify-between gap-3 py-2.5">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant="warning">
                      {CANCELLATION_LABEL[cancellation.status ?? ''] ?? cancellation.status}
                    </Badge>
                    <span className="text-caption text-ink-muted">
                      {cancellation.createdAt ? new Date(cancellation.createdAt).toLocaleString('es-BO') : ''}
                      {cancellation.resolutionAttempts
                        ? ` · ${cancellation.resolutionAttempts} intento(s) de resolución`
                        : ''}
                    </span>
                  </div>
                  {cancellation.failureReason && (
                    <p className="mt-1 text-label text-ink-muted">{cancellation.failureReason}</p>
                  )}
                </div>
                <Link
                  to={`/admin/reservations/${cancellation.reservationId}`}
                  className="text-label font-medium text-primary underline-offset-4 hover:underline"
                >
                  Ver pagos
                </Link>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
