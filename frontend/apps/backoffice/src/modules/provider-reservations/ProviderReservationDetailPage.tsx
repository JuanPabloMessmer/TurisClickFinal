import { formatCurrency, formatDate, formatDateTime } from '@turisclick/utils'
import { ArrowLeft } from 'lucide-react'
import { Link, useParams } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { PageHeader } from '@/components/PageHeader'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { useCompanyReservation } from './api'

type BadgeVariant = 'success' | 'warning' | 'destructive' | 'neutral'

const itemStatusVariant: Record<string, BadgeVariant> = {
  CONFIRMED: 'success',
  PENDING_PAYMENT: 'warning',
  CANCELLED: 'neutral',
}
const itemStatusLabel: Record<string, string> = {
  CONFIRMED: 'Confirmada',
  PENDING_PAYMENT: 'Pago pendiente',
  CANCELLED: 'Cancelada',
}

const reservationStatusVariant: Record<string, BadgeVariant> = {
  CONFIRMED: 'success',
  PENDING_PAYMENT: 'warning',
  PAYMENT_FAILED: 'destructive',
  CANCELLED: 'neutral',
  EXPIRED: 'neutral',
}
const reservationStatusLabel: Record<string, string> = {
  CONFIRMED: 'Confirmada',
  PENDING_PAYMENT: 'Pago pendiente',
  PAYMENT_FAILED: 'Pago fallido',
  CANCELLED: 'Cancelada',
  EXPIRED: 'Expirada',
}

export function ProviderReservationDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { data: item, isLoading } = useCompanyReservation(id)

  if (isLoading || !item) return <FullScreenSpinner />

  return (
    <div className="max-w-xl">
      <Link
        to="/provider/reservations"
        className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a Reservas
      </Link>
      <PageHeader title="Detalle de la reserva" description="UC-P-13." />

      <Card>
        <CardContent className="grid grid-cols-1 gap-5 pt-6 text-sm sm:grid-cols-2">
          <Row label="Experiencia" value={item.experienceTitle} />
          <Row label="Turista" value={item.touristName} />
          <Row label="Fecha" value={item.date ? formatDate(item.date) : undefined} />
          <Row label="Hora" value={item.startTime ?? 'Día completo'} />
          <Row label="Viajeros" value={String(item.travelers)} />
          <Row label="Precio unitario" value={formatCurrency(item.unitPrice ?? 0, item.currency ?? 'USD')} />
          <Row label="Subtotal" value={formatCurrency(item.subtotal ?? 0, item.currency ?? 'USD')} />
          <Row label="Creada" value={item.createdAt ? formatDateTime(item.createdAt) : '—'} />
          <div>
            <p className="text-muted-foreground">Estado del ítem</p>
            <Badge className="mt-1" variant={itemStatusVariant[item.status ?? ''] ?? 'neutral'}>
              {itemStatusLabel[item.status ?? ''] ?? item.status}
            </Badge>
          </div>
          <div>
            <p className="text-muted-foreground">Estado de la reserva</p>
            <Badge className="mt-1" variant={reservationStatusVariant[item.reservationStatus ?? ''] ?? 'neutral'}>
              {reservationStatusLabel[item.reservationStatus ?? ''] ?? item.reservationStatus}
            </Badge>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

function Row({ label, value }: { label: string; value?: string | null }) {
  return (
    <div>
      <p className="text-muted-foreground">{label}</p>
      <p className="font-medium text-foreground">{value ?? '—'}</p>
    </div>
  )
}
