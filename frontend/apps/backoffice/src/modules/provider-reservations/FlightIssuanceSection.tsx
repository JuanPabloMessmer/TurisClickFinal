import type { ReservationItemResponse } from '@turisclick/api-client'
import { formatDate, formatDateTime } from '@turisclick/utils'
import { Ticket } from 'lucide-react'
import { useRef, useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { useRecordIssuance, useRecordIssuanceFailure } from '@/modules/provider-flights/api'
import { DemoBadge } from '@/modules/provider-flights/DemoBadge'
import { flightErrorMessage } from '@/modules/provider-flights/flightErrors'
import { TICKET_MAX, validateTickets } from './issuance'
import { cabinLabel, flightCode, formatLocalDateTime, issuanceLabel, issuanceVariant, passengerTypeLabel } from '@/modules/provider-flights/labels'

const REASON_MIN = 5
const REASON_MAX = 500

/**
 * La emisión de un vuelo de operador. TurisClick no emite: el operador emite el pasaje fuera de la plataforma y
 * registra acá el resultado. Hasta que lo registra, ninguna pantalla dice "emitido".
 */
export function FlightIssuanceSection({ item }: { item: ReservationItemResponse }) {
  const flight = item.flight!
  const passengers = flight.passengers ?? []
  const status = flight.issuanceStatus ?? 'NOT_REQUIRED'
  const [dialog, setDialog] = useState<'issue' | 'fail' | null>(null)
  const canRecord = item.status === 'CONFIRMED' && status === 'PENDING'

  return (
    <Card className="mt-6">
      <CardContent className="flex flex-col gap-4 pt-6 text-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="font-medium text-foreground">Vuelo</p>
          <div className="flex items-center gap-2">
            <DemoBadge />
            <Badge variant={issuanceVariant[status] ?? 'neutral'}>{issuanceLabel[status] ?? status}</Badge>
          </div>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Row label="Ruta" value={`${flight.originIata} → ${flight.destinationIata}`} hint={`${flight.originLabel ?? ''} → ${flight.destinationLabel ?? ''}`} />
          <Row label="Vuelo" value={`${flight.carrierName ?? ''} ${flightCode(flight.carrierIata, flight.flightNumber)}`.trim()} />
          <Row label="Salida (hora local)" value={formatLocalDateTime(flight.departureAt)} />
          <Row label="Llegada (hora local)" value={formatLocalDateTime(flight.arrivalAt)} />
          <Row label="Cabina" value={cabinLabel[flight.cabinClass ?? ''] ?? flight.cabinClass} />
          <Row label="Equipaje" value={`${flight.checkedBags ?? 0} despachada(s) · ${flight.carryOnBags ?? 0} de mano`} />
        </div>

        <div>
          <p className="mb-2 text-muted-foreground">Pasajeros</p>
          <ul className="flex flex-col gap-1.5">
            {passengers.map((p, index) => (
              <li key={`${p.givenName}-${p.familyName}-${index}`} className="flex flex-wrap items-baseline justify-between gap-2">
                <span className="font-medium text-foreground">
                  {p.givenName} {p.familyName}
                  <span className="ml-2 text-xs font-normal text-muted-foreground">
                    {passengerTypeLabel[p.type ?? ''] ?? p.type}
                    {p.bornOn ? ` · nacido el ${formatDate(p.bornOn)}` : ''}
                  </span>
                </span>
                {status === 'ISSUED' && flight.ticketNumbers?.[index] && (
                  <span className="font-mono text-xs text-foreground">{flight.ticketNumbers[index]}</span>
                )}
              </li>
            ))}
          </ul>
        </div>

        {status === 'NOT_REQUIRED' && (
          <p className="text-muted-foreground">Todavía no hay nada que emitir: el pasaje se emite cuando el viajero paga.</p>
        )}
        {status === 'PENDING' && item.status === 'CONFIRMED' && (
          <Alert variant="warning">
            El viajero pagó y espera su pasaje. Emitilo con la aerolínea y registrá acá los números, o avisá si no pudiste.
          </Alert>
        )}
        {status === 'ISSUED' && (
          <p className="text-muted-foreground">
            Registraste la emisión
            {flight.issuedAt ? ` el ${formatDateTime(flight.issuedAt)}` : ''}
            {flight.bookingReference ? ` con el localizador ${flight.bookingReference}` : ''}. Es un pasaje de demostración: no es un pasaje real.
          </p>
        )}
        {status === 'FAILED' && (
          <p className="text-muted-foreground">
            Registraste que no pudiste emitir. El servicio quedó cancelado con reembolso total (simulado) y los asientos se liberaron.
          </p>
        )}

        {canRecord && (
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => setDialog('issue')}>
              <Ticket className="h-4 w-4" aria-hidden="true" />
              Registrar emisión
            </Button>
            <Button variant="outline" onClick={() => setDialog('fail')}>
              No pude emitir
            </Button>
          </div>
        )}
      </CardContent>

      <Dialog open={!!dialog} onOpenChange={(open) => !open && setDialog(null)}>
        {dialog === 'issue' && <IssueDialogContent item={item} onClose={() => setDialog(null)} />}
        {dialog === 'fail' && <FailureDialogContent itemId={item.id!} onClose={() => setDialog(null)} />}
      </Dialog>
    </Card>
  )
}

function IssueDialogContent({ item, onClose }: { item: ReservationItemResponse; onClose: () => void }) {
  const passengers = item.flight?.passengers ?? []
  const record = useRecordIssuance()
  const [tickets, setTickets] = useState<string[]>(passengers.map(() => ''))
  const [bookingReference, setBookingReference] = useState('')
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const submittingRef = useRef(false)

  const onSave = async () => {
    if (submittingRef.current) return
    setError(null)
    const invalid = validateTickets(tickets)
    if (invalid) {
      setError(invalid)
      return
    }
    submittingRef.current = true
    try {
      await record.mutateAsync({
        itemId: item.id!,
        body: {
          ticketNumbers: tickets.map((t) => t.trim().toUpperCase()),
          bookingReference: bookingReference.trim() || null,
          note: note.trim() || null,
        },
      })
      onClose()
    } catch (err) {
      setError(flightErrorMessage(err))
    } finally {
      submittingRef.current = false
    }
  }

  return (
    <DialogContent className="max-w-lg">
      <DialogHeader>
        <DialogTitle>Registrar la emisión</DialogTitle>
        <DialogDescription>
          Cargá el número de pasaje que te dio la aerolínea para cada pasajero, infantes incluidos. El viajero lo va a
          ver en su reserva. En esta demostración, el pasaje queda rotulado DEMO.
        </DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-3">
        {passengers.map((p, index) => (
          <div key={`${p.givenName}-${index}`} className="flex flex-col gap-1.5">
            <Label htmlFor={`ticket-${index}`}>
              Pasaje de {p.givenName} {p.familyName} ({passengerTypeLabel[p.type ?? ''] ?? p.type})
            </Label>
            <Input
              id={`ticket-${index}`}
              value={tickets[index]}
              maxLength={TICKET_MAX}
              className="font-mono"
              onChange={(e) => setTickets((current) => current.map((t, i) => (i === index ? e.target.value : t)))}
            />
          </div>
        ))}
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="booking-reference">Localizador (opcional)</Label>
          <Input id="booking-reference" value={bookingReference} maxLength={20} onChange={(e) => setBookingReference(e.target.value.toUpperCase())} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="issuance-note">Nota (opcional)</Label>
          <Textarea id="issuance-note" rows={2} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
        </div>
      </div>
      {error && <Alert variant="destructive">{error}</Alert>}
      <DialogFooter>
        <Button variant="outline" onClick={onClose} disabled={record.isPending}>
          Volver
        </Button>
        <Button loading={record.isPending} onClick={() => void onSave()}>
          Registrar emisión
        </Button>
      </DialogFooter>
    </DialogContent>
  )
}

function FailureDialogContent({ itemId, onClose }: { itemId: string; onClose: () => void }) {
  const fail = useRecordIssuanceFailure()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const submittingRef = useRef(false)

  const onConfirm = async () => {
    if (submittingRef.current) return
    setError(null)
    const trimmed = reason.trim()
    if (trimmed.length < REASON_MIN) {
      setError(`Contale al viajero qué pasó: al menos ${REASON_MIN} caracteres.`)
      return
    }
    submittingRef.current = true
    try {
      await fail.mutateAsync({ itemId, reason: trimmed })
      onClose()
    } catch (err) {
      setError(flightErrorMessage(err))
    } finally {
      submittingRef.current = false
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>¿No pudiste emitir el pasaje?</DialogTitle>
        <DialogDescription>
          El servicio se cancela con reembolso total al viajero (simulado en esta demostración) y los asientos vuelven a
          la venta. No se puede deshacer.
        </DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="failure-reason">Qué pasó (lo ve el viajero)</Label>
        <Textarea
          id="failure-reason"
          rows={3}
          maxLength={REASON_MAX}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="ej. La aerolínea canceló la salida"
        />
      </div>
      {error && <Alert variant="destructive">{error}</Alert>}
      <DialogFooter>
        <Button variant="outline" onClick={onClose} disabled={fail.isPending}>
          Volver
        </Button>
        <Button variant="destructive" loading={fail.isPending} onClick={() => void onConfirm()}>
          Registrar que no se emitió
        </Button>
      </DialogFooter>
    </DialogContent>
  )
}

function Row({ label, value, hint }: { label: string; value?: string | null; hint?: string }) {
  return (
    <div>
      <p className="text-muted-foreground">{label}</p>
      <p className="font-medium text-foreground">{value ?? '—'}</p>
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  )
}
