import { formatCurrency } from '@turisclick/utils'
import { Plane, TriangleAlert } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableEmptyRow,
  TableHead,
  TableHeader,
  TableLoadingRow,
  TableRow,
} from '@/components/ui/table'
import { useAdminReservations } from './api'

/**
 * Todas las reservas de la plataforma.
 *
 * El filtro "Necesita atención" es lo que convierte esta tabla en una cola de trabajo y no en un reporte: deja
 * sólo lo que está esperando a alguien — un pasaje sin resolver o una cancelación que quedó a medias—.
 *
 * No muestra datos de pasajeros: no existen en la base, y tener un rol global no es razón para empezar a
 * guardarlos.
 */

const PAGE_SIZE = 20

export const RESERVATION_LABEL: Record<string, string> = {
  PENDING_PAYMENT: 'Pendiente de pago',
  CONFIRMED: 'Confirmada',
  CANCELLING: 'Cancelando',
  CANCELLED: 'Cancelada',
  EXPIRED: 'Expirada',
  PAYMENT_FAILED: 'Pago rechazado',
}

export const RESERVATION_VARIANT: Record<string, 'success' | 'warning' | 'destructive' | 'neutral'> = {
  PENDING_PAYMENT: 'warning',
  CONFIRMED: 'success',
  CANCELLING: 'warning',
  CANCELLED: 'destructive',
  EXPIRED: 'neutral',
  PAYMENT_FAILED: 'destructive',
}

export const FLIGHT_LABEL: Record<string, string> = {
  PENDING: 'Pasaje pendiente',
  ORDERING: 'Emitiendo',
  CONFIRMED: 'Pasaje emitido',
  FAILED: 'No se emitió',
  RECONCILIATION_REQUIRED: 'Confirmando con la aerolínea',
  CANCELLED: 'Pasaje cancelado',
}

export const CANCELLATION_LABEL: Record<string, string> = {
  QUOTED: 'Presupuestada',
  ACCEPTED: 'En proceso',
  COMPLETED: 'Completada',
  REFUND_PENDING: 'Reembolso pendiente',
  REQUIRES_REVIEW: 'Requiere revisión',
  FAILED: 'No se canceló',
  EXPIRED: 'Presupuesto vencido',
}

const KIND_LABEL: Record<string, string> = {
  PACKAGE: 'Paquete',
  EXPERIENCE: 'Experiencia',
  MIXED: 'Varios servicios',
}

/** Lo que pide acción: un pasaje sin resolver o una cancelación a medias. */
export function needsAttention(row: { flightStatus?: string | null; cancellationStatus?: string | null }) {
  return (
    row.flightStatus === 'RECONCILIATION_REQUIRED' ||
    row.flightStatus === 'ORDERING' ||
    row.cancellationStatus === 'REFUND_PENDING' ||
    row.cancellationStatus === 'REQUIRES_REVIEW' ||
    row.cancellationStatus === 'ACCEPTED'
  )
}

export function AdminReservationsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const status = searchParams.get('status') ?? 'ALL'
  const attention = searchParams.get('attention') === '1'
  const page = Math.max(Number(searchParams.get('page') ?? '1'), 1)

  const { data, isLoading } = useAdminReservations({
    status: status === 'ALL' ? undefined : status,
    needsAttention: attention ? true : undefined,
    page,
    pageSize: PAGE_SIZE,
  })

  const items = data?.items ?? []
  const totalPages = data?.totalPages ?? 1

  const update = (key: string, value: string | null) =>
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous)
      if (value === null) next.delete(key)
      else next.set(key, value)
      next.set('page', '1')
      return next
    })

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Reservas"
        description="Todas las reservas de la plataforma, con el estado del pasaje, de la cancelación y del cobro."
      />

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <Select value={status} onValueChange={(value) => update('status', value === 'ALL' ? null : value)}>
          <SelectTrigger className="w-full sm:w-56" aria-label="Filtrar por estado">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="ALL">Todos los estados</SelectItem>
            <SelectItem value="PENDING_PAYMENT">Pendientes de pago</SelectItem>
            <SelectItem value="CONFIRMED">Confirmadas</SelectItem>
            <SelectItem value="CANCELLED">Canceladas</SelectItem>
            <SelectItem value="EXPIRED">Expiradas</SelectItem>
          </SelectContent>
        </Select>

        <Button
          variant={attention ? 'default' : 'outline'}
          onClick={() => update('attention', attention ? null : '1')}
          aria-pressed={attention}
        >
          <TriangleAlert className="h-4 w-4" aria-hidden="true" />
          Necesita atención
        </Button>
      </div>

      <Card className="overflow-hidden p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Reserva</TableHead>
              <TableHead>Viajero</TableHead>
              <TableHead>Empresa</TableHead>
              <TableHead>Vuelo</TableHead>
              <TableHead className="text-right">Cobrado</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading ? (
              <TableLoadingRow colSpan={7} />
            ) : items.length === 0 ? (
              <TableEmptyRow
                colSpan={7}
                title={attention ? 'No hay nada esperando resolución' : 'Todavía no hay reservas'}
                description={
                  attention
                    ? 'Ningún pasaje quedó sin resolver y ninguna cancelación quedó a medias.'
                    : 'Cuando un viajero reserve, va a aparecer acá.'
                }
              />
            ) : (
              items.map((row) => (
                <TableRow key={row.id}>
                  <TableCell>
                    <span className="block font-medium text-foreground">{row.summary}</span>
                    <span className="text-caption text-ink-muted">
                      {KIND_LABEL[row.kind ?? ''] ?? row.kind}
                      {row.fromAssistant ? ' · desde el asistente' : ''} ·{' '}
                      {row.createdAt ? new Date(row.createdAt).toLocaleDateString('es-BO') : ''}
                    </span>
                  </TableCell>
                  <TableCell>{row.touristName}</TableCell>
                  <TableCell className="text-label">{(row.companies ?? []).join(', ')}</TableCell>
                  <TableCell>
                    {row.flightStatus ? (
                      <span className="flex items-center gap-1.5 text-label">
                        <Plane className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                        <span>
                          {FLIGHT_LABEL[row.flightStatus] ?? row.flightStatus}
                          {row.flightRoute ? <span className="block text-caption text-ink-muted">{row.flightRoute}</span> : null}
                        </span>
                      </span>
                    ) : (
                      <span className="text-ink-muted">—</span>
                    )}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {(row.charged ?? []).length === 0 ? (
                      <span className="text-ink-muted">—</span>
                    ) : (
                      (row.charged ?? []).map((money) => (
                        <span key={money.currency} className="block">
                          {formatCurrency(money.amount ?? 0, money.currency ?? 'BOB')}
                        </span>
                      ))
                    )}
                    {/* Lo devuelto se muestra aparte: nunca se resta para mostrar un solo número. */}
                    {(row.refunded ?? []).map((money) => (
                      <span key={money.currency} className="block text-caption text-ink-muted">
                        −{formatCurrency(money.amount ?? 0, money.currency ?? 'BOB')}
                      </span>
                    ))}
                  </TableCell>
                  <TableCell>
                    <div className="flex flex-col gap-1">
                      <Badge variant={RESERVATION_VARIANT[row.status ?? ''] ?? 'neutral'}>
                        {RESERVATION_LABEL[row.status ?? ''] ?? row.status}
                      </Badge>
                      {row.cancellationStatus && row.cancellationStatus !== 'COMPLETED' && (
                        <span className="text-caption text-warning-fg">
                          {CANCELLATION_LABEL[row.cancellationStatus] ?? row.cancellationStatus}
                        </span>
                      )}
                    </div>
                  </TableCell>
                  <TableCell className="text-right">
                    <Link
                      to={`/admin/reservations/${row.id}`}
                      className="text-label font-medium text-primary underline-offset-4 hover:underline"
                    >
                      Ver pagos
                    </Link>
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>

        {totalPages > 1 && (
          <div className="flex items-center justify-between gap-3 border-t border-border px-4 py-3">
            <p className="text-label text-ink-muted">
              Página {page} de {totalPages}
            </p>
            <div className="flex gap-2">
              <Button
                variant="outline"
                disabled={page <= 1}
                onClick={() =>
                  setSearchParams((previous) => {
                    const next = new URLSearchParams(previous)
                    next.set('page', String(page - 1))
                    return next
                  })
                }
              >
                Anterior
              </Button>
              <Button
                variant="outline"
                disabled={page >= totalPages}
                onClick={() =>
                  setSearchParams((previous) => {
                    const next = new URLSearchParams(previous)
                    next.set('page', String(page + 1))
                    return next
                  })
                }
              >
                Siguiente
              </Button>
            </div>
          </div>
        )}
      </Card>
    </div>
  )
}
