import { formatCurrency, formatDate, formatDateTime } from '@turisclick/utils'
import { ArrowLeft } from 'lucide-react'
import { useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { getErrorMessage } from '@/lib/errors'
import { useCancelCompanyReservation, useCompanyReservation } from './api'

/** Estados de la línea que el backend deja cancelar al proveedor (UC-P-14); cualquier otro responde 409. */
const CANCELLABLE_ITEM_STATUSES = ['PENDING_PAYMENT', 'CONFIRMED']
const REASON_MIN = 5
const REASON_MAX = 500

type BadgeVariant = 'success' | 'warning' | 'destructive' | 'neutral'

const itemStatusVariant: Record<string, BadgeVariant> = {
  CONFIRMED: 'success',
  PENDING_PAYMENT: 'warning',
  CANCELLED: 'neutral',
  EXPIRED: 'neutral',
}
const itemStatusLabel: Record<string, string> = {
  CONFIRMED: 'Confirmada',
  PENDING_PAYMENT: 'Pago pendiente',
  CANCELLED: 'Cancelada',
  EXPIRED: 'Expirada',
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
  const [cancelOpen, setCancelOpen] = useState(false)

  if (isLoading || !item) return <FullScreenSpinner />

  const canCancel = CANCELLABLE_ITEM_STATUSES.includes(item.status ?? '')

  return (
    <div className="max-w-xl">
      <Link
        to="/provider/reservations"
        className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a Reservas
      </Link>
      <PageHeader title="Detalle de la reserva" description="Qué se reservó, quién viaja y en qué estado está." />

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
          {item.status === 'CANCELLED' && (
            <>
              <Row label="Cancelada el" value={item.cancelledAt ? formatDateTime(item.cancelledAt) : undefined} />
              <Row label="Motivo de la cancelación" value={item.cancellationReason} />
            </>
          )}
        </CardContent>
      </Card>

      {canCancel && id && (
        <Card className="mt-6">
          <CardContent className="flex flex-col gap-3 pt-6 text-sm">
            <div>
              <p className="font-medium text-foreground">Cancelar esta reserva</p>
              <p className="text-muted-foreground">
                Sólo para casos de fuerza mayor, cuando no vas a poder prestar el servicio.
              </p>
            </div>
            <Button variant="destructive" className="self-start" onClick={() => setCancelOpen(true)}>
              Cancelar reserva
            </Button>
          </CardContent>
        </Card>
      )}

      <Dialog open={cancelOpen} onOpenChange={setCancelOpen}>
        {cancelOpen && id && <CancelReservationDialogContent itemId={id} onClose={() => setCancelOpen(false)} />}
      </Dialog>
    </div>
  )
}

/** UC-P-14 — cancelación excepcional. Lo que se le explica al operador es lo que hace el backend, nada más. */
function CancelReservationDialogContent({ itemId, onClose }: { itemId: string; onClose: () => void }) {
  const cancelMutation = useCancelCompanyReservation()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  // isPending recién se ve en el próximo render: el ref corta un doble clic antes de que llegue.
  const submittingRef = useRef(false)

  const onConfirm = async () => {
    if (submittingRef.current) return
    setError(null)
    const trimmed = reason.trim()
    if (trimmed.length < REASON_MIN) {
      setError(`El motivo debe tener al menos ${REASON_MIN} caracteres.`)
      return
    }
    submittingRef.current = true
    try {
      await cancelMutation.mutateAsync({ id: itemId, reason: trimmed })
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    } finally {
      submittingRef.current = false
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>¿Cancelar esta reserva?</DialogTitle>
        <DialogDescription>
          Es una medida excepcional y no se puede deshacer. Se libera el cupo de la salida y el viajero ve este
          servicio como cancelado, junto con el motivo que escribas. El resto de su reserva, si incluye servicios
          de otros operadores, sigue igual. Esta cancelación no genera un reembolso automático.
        </DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cancel-reason">Motivo (lo ve el viajero)</Label>
        <Textarea
          id="cancel-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={3}
          maxLength={REASON_MAX}
          placeholder="ej. Cierre del parque por alerta meteorológica"
        />
      </div>
      {error && (
        <Alert variant="destructive" className="mt-2">
          {error}
        </Alert>
      )}
      <DialogFooter>
        <Button variant="outline" onClick={onClose} disabled={cancelMutation.isPending}>
          Volver
        </Button>
        <Button variant="destructive" onClick={() => void onConfirm()} disabled={cancelMutation.isPending}>
          {cancelMutation.isPending ? 'Cancelando…' : 'Sí, cancelar reserva'}
        </Button>
      </DialogFooter>
    </DialogContent>
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
