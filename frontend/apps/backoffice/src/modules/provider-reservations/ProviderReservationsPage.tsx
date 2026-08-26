import { formatCurrency, formatDate } from '@turisclick/utils'
import { ChevronRight, ClipboardList } from 'lucide-react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { useCompanyReservations } from './api'

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
      <PageHeader title="Reservas" description="UC-P-12 — reservas recibidas por tu empresa." />

      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Experiencia</TableHead>
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
                <TableCell className="font-medium">{item.experienceTitle}</TableCell>
                <TableCell className="text-muted-foreground">{item.touristName}</TableCell>
                <TableCell>{item.date ? formatDate(item.date) : '—'}</TableCell>
                <TableCell>{item.travelers}</TableCell>
                <TableCell>{formatCurrency(item.subtotal ?? 0, item.currency ?? 'USD')}</TableCell>
                <TableCell>
                  <Badge variant={statusVariant[item.status ?? ''] ?? 'neutral'}>{statusLabel[item.status ?? ''] ?? item.status}</Badge>
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
