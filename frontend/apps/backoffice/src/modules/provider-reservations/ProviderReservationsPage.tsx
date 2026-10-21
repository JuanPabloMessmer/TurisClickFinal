import { formatCurrency, formatDate } from '@turisclick/utils'
import { ChevronRight, ClipboardList } from 'lucide-react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { formatLocalDateTime, issuanceLabel, issuanceVariant } from '@/modules/provider-flights/labels'
import { useCompanyReservations } from './api'
import { isFlightItem, itemTitle } from './itemTitle'

const statusVariant: Record<string, 'success' | 'warning' | 'neutral'> = {
  CONFIRMED: 'success',
  PENDING_PAYMENT: 'warning',
  CANCELLED: 'neutral',
}

const statusLabel: Record<string, string> = {
  CONFIRMED: 'Confirmada',
  PENDING_PAYMENT: 'Pago pendiente',
  CANCELLED: 'Cancelada',
}

export function ProviderReservationsPage() {
  const { data, isLoading } = useCompanyReservations()
  const items = data?.items ?? []

  return (
    <div>
      <PageHeader title="Reservas" description="Las reservas que recibieron tus experiencias, paquetes y vuelos." />

      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Producto</TableHead>
              <TableHead>Turista</TableHead>
              <TableHead>Fecha</TableHead>
              <TableHead>Viajeros</TableHead>
              <TableHead>Subtotal</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={7} />}
            {!isLoading && items.length === 0 && (
              <TableEmptyRow colSpan={7} icon={ClipboardList} title="Todavía no recibiste reservas" />
            )}
            {items.map((item) => (
              <TableRow key={item.id}>
                <TableCell className="font-medium">{itemTitle(item)}</TableCell>
                <TableCell className="text-muted-foreground">{item.touristName}</TableCell>
                <TableCell>
                  {isFlightItem(item) ? formatLocalDateTime(item.flight?.departureAt) : item.date ? formatDate(item.date) : '—'}
                </TableCell>
                <TableCell>{item.travelers}</TableCell>
                <TableCell>{formatCurrency(item.subtotal ?? 0, item.currency ?? 'USD')}</TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-1">
                    <Badge variant={statusVariant[item.status ?? ''] ?? 'neutral'}>{statusLabel[item.status ?? ''] ?? item.status}</Badge>
                    {/* Sólo lo que pide una acción del operador o ya la tuvo: NOT_REQUIRED (sin pagar) no se muestra. */}
                    {isFlightItem(item) && item.flight?.issuanceStatus && item.flight.issuanceStatus !== 'NOT_REQUIRED' && (
                      <Badge variant={issuanceVariant[item.flight.issuanceStatus] ?? 'neutral'}>
                        {issuanceLabel[item.flight.issuanceStatus] ?? item.flight.issuanceStatus}
                      </Badge>
                    )}
                  </div>
                </TableCell>
                <TableCell>
                  <Link
                    to={`/provider/reservations/${item.id}`}
                    className="inline-flex items-center gap-0.5 text-sm font-medium text-primary hover:underline"
                  >
                    Ver detalle
                    <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </Link>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
