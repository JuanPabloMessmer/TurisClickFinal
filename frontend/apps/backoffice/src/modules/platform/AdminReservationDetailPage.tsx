import { ArrowLeft } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import {
  Table,
  TableBody,
  TableCell,
  TableEmptyRow,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { CANCELLATION_LABEL, RESERVATION_LABEL, RESERVATION_VARIANT } from './AdminReservationsPage'
import { useReservationPayments } from './api'

/**
 * El libro de pagos de una reserva: qué se cobró, qué se devolvió y qué quedó.
 *
 * Es una vista de **operación**, no de contabilidad: responde "qué pasó con la plata de esta reserva" para
 * poder resolver un caso. Incluye los intentos fallidos a propósito —son la única forma de saber qué operación
 * no funcionó— y no permite editar nada: resolver una cancelación a medias es una operación de dominio, no un
 * ajuste manual sobre una tabla.
 *
 * No muestra ningún dato de medio de pago, porque el libro no los guarda.
 */

const TYPE_LABEL: Record<string, string> = {
  CHARGE: 'Cobro',
  REFUND: 'Reembolso',
  VOID: 'Reverso',
}

const TYPE_VARIANT: Record<string, 'success' | 'warning' | 'destructive' | 'neutral'> = {
  CHARGE: 'neutral',
  REFUND: 'success',
  VOID: 'warning',
}

const COMPONENT_LABEL: Record<string, string> = {
  PACKAGE: 'Paquete',
  EXPERIENCE: 'Experiencia',
  FLIGHT: 'Vuelo',
}

const STATUS_VARIANT: Record<string, 'success' | 'warning' | 'destructive' | 'neutral'> = {
  SUCCEEDED: 'success',
  PENDING: 'warning',
  FAILED: 'destructive',
}

const STATUS_LABEL: Record<string, string> = {
  SUCCEEDED: 'Hecho',
  PENDING: 'En curso',
  FAILED: 'Falló',
}

export function AdminReservationDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const { data, isLoading } = useReservationPayments(id)

  if (isLoading) {
    return (
      <div className="flex justify-center py-12">
        <Spinner />
      </div>
    )
  }

  if (!data) {
    return (
      <div className="flex flex-col gap-4">
        <PageHeader title="Reserva" description="No encontramos esta reserva." />
        <Link to="/admin/reservations" className="text-label font-medium text-primary hover:underline">
          Volver a reservas
        </Link>
      </div>
    )
  }

  const transactions = data.transactions ?? []
  const cancellations = data.cancellations ?? []

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-2">
        <Link
          to="/admin/reservations"
          className="flex items-center gap-1.5 text-label font-medium text-primary hover:underline"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Reservas
        </Link>
        <PageHeader
          title="Pagos de la reserva"
          description="Lo cobrado, lo devuelto y lo que quedó. Cada movimiento es una fila: el historial no se reescribe."
        />
        <Badge variant={RESERVATION_VARIANT[data.reservationStatus ?? ''] ?? 'neutral'} className="self-start">
          {RESERVATION_LABEL[data.reservationStatus ?? ''] ?? data.reservationStatus}
        </Badge>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-heading">Saldo</CardTitle>
        </CardHeader>
        <CardContent>
          {(data.balances ?? []).length === 0 ? (
            <p className="text-body text-ink-muted">Esta reserva todavía no tuvo movimientos de dinero.</p>
          ) : (
            <div className="grid gap-4 sm:grid-cols-3">
              {(data.balances ?? []).map((balance) => (
                <div key={balance.currency} className="rounded-sm border border-border p-3">
                  <p className="text-label font-medium text-foreground">{balance.currency}</p>
                  <dl className="mt-2 flex flex-col gap-1">
                    <div className="flex justify-between text-label">
                      <dt className="text-ink-muted">Cobrado</dt>
                      <dd className="tabular-nums">{(balance.charged ?? 0).toFixed(2)}</dd>
                    </div>
                    <div className="flex justify-between text-label">
                      <dt className="text-ink-muted">Devuelto</dt>
                      <dd className="tabular-nums">{(balance.refunded ?? 0).toFixed(2)}</dd>
                    </div>
                    <div className="flex justify-between border-t border-border pt-1 text-label font-semibold">
                      <dt>Queda pagado</dt>
                      <dd className="tabular-nums">{(balance.net ?? 0).toFixed(2)}</dd>
                    </div>
                  </dl>
                </div>
              ))}
            </div>
          )}
          {/* Monedas distintas no se suman: cada una tiene su propia tarjeta. */}
          {(data.balances ?? []).length > 1 && (
            <p className="mt-3 text-caption text-ink-muted">
              Son monedas distintas, así que no hay un total único: no existe conversión en TurisClick.
            </p>
          )}
        </CardContent>
      </Card>

      <Card className="overflow-hidden p-0">
        <div className="border-b border-border px-4 py-3">
          <h2 className="text-heading font-semibold text-foreground">Movimientos</h2>
          <p className="text-label text-ink-muted">
            Incluye los intentos fallidos: son la única forma de saber después qué operación no funcionó.
          </p>
        </div>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Fecha</TableHead>
              <TableHead>Movimiento</TableHead>
              <TableHead>Componente</TableHead>
              <TableHead className="text-right">Importe</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Motivo</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {transactions.length === 0 ? (
              <TableEmptyRow colSpan={6} title="Sin movimientos" description="Esta reserva todavía no tuvo cobros ni reembolsos." />
            ) : (
              transactions.map((movement) => (
                <TableRow key={movement.id}>
                  <TableCell className="text-label text-ink-muted">
                    {movement.createdAt ? new Date(movement.createdAt).toLocaleString('es-BO') : ''}
                  </TableCell>
                  <TableCell>
                    <Badge variant={TYPE_VARIANT[movement.type ?? ''] ?? 'neutral'}>
                      {TYPE_LABEL[movement.type ?? ''] ?? movement.type}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    {movement.component ? COMPONENT_LABEL[movement.component] ?? movement.component : '—'}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {movement.currency} {(movement.amount ?? 0).toFixed(2)}
                  </TableCell>
                  <TableCell>
                    <Badge variant={STATUS_VARIANT[movement.status ?? ''] ?? 'neutral'}>
                      {STATUS_LABEL[movement.status ?? ''] ?? movement.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="max-w-xs text-label text-ink-muted">{movement.failureReason ?? '—'}</TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </Card>

      {cancellations.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-heading">Cancelaciones</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {cancellations.map((cancellation) => (
              <div key={cancellation.id} className="rounded-sm border border-border p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <Badge variant={cancellation.status === 'COMPLETED' ? 'success' : 'warning'}>
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
                  <p className="mt-2 text-label text-warning-fg">{cancellation.failureReason}</p>
                )}

                <ul className="mt-2 flex flex-col gap-1">
                  {(cancellation.lines ?? []).map((line, index) => (
                    <li key={index} className="flex flex-wrap justify-between gap-2 text-label">
                      <span>
                        {COMPONENT_LABEL[line.component ?? ''] ?? line.component} · {line.label}
                      </span>
                      <span className="tabular-nums">
                        {line.refundKnown === false
                          ? 'Reembolso a confirmar'
                          : `${line.currency} ${(line.refundAmount ?? 0).toFixed(2)} de ${(line.paidAmount ?? 0).toFixed(2)}`}
                        {line.refundPercentage != null ? ` (${line.refundPercentage}%)` : ''}
                      </span>
                    </li>
                  ))}
                </ul>

                {cancellation.flightCancelled && (
                  <p className="mt-2 text-caption text-ink-muted">El pasaje quedó cancelado en la aerolínea.</p>
                )}
              </div>
            ))}
          </CardContent>
        </Card>
      )}
    </div>
  )
}
