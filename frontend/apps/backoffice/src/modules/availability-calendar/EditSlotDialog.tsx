import { formatDate, todayIso } from '@turisclick/utils'
import { useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { getErrorMessage } from '@/lib/errors'
import type { CalendarSlot } from './AvailabilityMonthCalendar'

/**
 * Edición de una fecha puntual: cupo y abrir/cerrar. El cupo nunca baja de lo ya reservado (el backend lo
 * vuelve a validar de forma atómica) y cerrar solo frena reservas nuevas: las existentes se respetan.
 */
export function EditSlotDialog({
  slot,
  onClose,
  save,
}: {
  slot: CalendarSlot | null
  onClose: () => void
  save: (slot: CalendarSlot, body: { totalSlots?: number; status?: string }) => Promise<unknown>
}) {
  return (
    <Dialog open={slot !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent>{slot && <EditSlotForm key={slot.id} slot={slot} onClose={onClose} save={save} />}</DialogContent>
    </Dialog>
  )
}

function EditSlotForm({
  slot,
  onClose,
  save,
}: {
  slot: CalendarSlot
  onClose: () => void
  save: (slot: CalendarSlot, body: { totalSlots?: number; status?: string }) => Promise<unknown>
}) {
  const [totalSlots, setTotalSlots] = useState(slot.totalSlots)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const closed = slot.status === 'CLOSED'
  const past = slot.date < todayIso()

  const submit = async (body: { totalSlots?: number; status?: string }) => {
    setBusy(true)
    setError(null)
    try {
      await save(slot, body)
      onClose()
    } catch (e) {
      setError(getErrorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const tooLow = totalSlots < Math.max(1, slot.reservedSlots)

  return (
    <>
      <DialogHeader>
        <DialogTitle>
          {formatDate(slot.date)} · {slot.time ? slot.time.slice(0, 5) : 'Día completo'}
        </DialogTitle>
        <DialogDescription>
          {slot.reservedSlots} reservados · {slot.availableSlots} libres · {closed ? 'Cerrada' : 'Abierta'}
        </DialogDescription>
      </DialogHeader>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="slot-capacity">Capacidad total</Label>
        <Input
          id="slot-capacity"
          type="number"
          min={Math.max(1, slot.reservedSlots)}
          value={Number.isNaN(totalSlots) ? '' : totalSlots}
          onChange={(e) => setTotalSlots(e.target.valueAsNumber)}
        />
        {tooLow && <p className="text-xs text-destructive">No puede ser menor que los {slot.reservedSlots} lugares ya reservados.</p>}
      </div>

      {closed ? null : slot.reservedSlots > 0 ? (
        <p className="mt-3 text-xs text-muted-foreground">Si la cerrás, las {slot.reservedSlots} plazas reservadas se mantienen; solo se frenan reservas nuevas.</p>
      ) : null}

      {error && (
        <Alert variant="destructive" className="mt-3">
          {error}
        </Alert>
      )}

      <DialogFooter className="mt-5 flex flex-wrap justify-between gap-2">
        <Button
          type="button"
          variant="outline"
          disabled={busy || (closed && past)}
          onClick={() => void submit({ status: closed ? 'OPEN' : 'CLOSED' })}
        >
          {closed ? 'Reabrir fecha' : 'Cerrar fecha'}
        </Button>
        <Button type="button" disabled={busy || tooLow || totalSlots === slot.totalSlots} onClick={() => void submit({ totalSlots })}>
          Guardar capacidad
        </Button>
      </DialogFooter>
    </>
  )
}
