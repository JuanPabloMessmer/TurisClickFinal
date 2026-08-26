import { useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { getErrorMessage } from '@/lib/errors'
import { useCompany, useRejectCompany } from './api'

/** UC-A-03. Reutilizado por la lista y el detalle de empresa — recibe solo el id, resuelve el nombre él mismo. */
export function RejectDialog({ companyId, onClose }: { companyId: string | null; onClose: () => void }) {
  return (
    <Dialog
      open={!!companyId}
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      {companyId && <RejectDialogContent key={companyId} companyId={companyId} onClose={onClose} />}
    </Dialog>
  )
}

function RejectDialogContent({ companyId, onClose }: { companyId: string; onClose: () => void }) {
  const { data: company } = useCompany(companyId)
  const rejectMutation = useRejectCompany()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  const onConfirm = async () => {
    setError(null)
    if (reason.trim().length < 5) {
      setError('El motivo debe tener al menos 5 caracteres.')
      return
    }
    try {
      await rejectMutation.mutateAsync({ id: companyId, body: { reason } })
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Rechazar solicitud{company?.name ? ` de ${company.name}` : ''}</DialogTitle>
      </DialogHeader>
      <div className="flex flex-col gap-1.5">
        <Label>Motivo</Label>
        <Textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={3} />
      </div>
      {error && (
        <Alert variant="destructive" className="mt-2">
          {error}
        </Alert>
      )}
      <DialogFooter>
        <Button variant="outline" onClick={onClose}>
          Cancelar
        </Button>
        <Button variant="destructive" onClick={() => void onConfirm()} disabled={rejectMutation.isPending}>
          Rechazar
        </Button>
      </DialogFooter>
    </DialogContent>
  )
}
