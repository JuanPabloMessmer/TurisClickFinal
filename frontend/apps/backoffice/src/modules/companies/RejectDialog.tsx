import { LIMITS, multiLine, rules } from '@turisclick/validation'
import { useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Textarea } from '@/components/ui/textarea'
import { FormField } from '@/components/form/FormField'
import { fieldAria } from '@/components/form/fieldAria'
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
  const [fieldError, setFieldError] = useState<string | null>(null)

  const onConfirm = async () => {
    setError(null)
    // R-10 (docs/auditoria-validaciones.md): la misma regla que valida el backend.
    const invalid = rules.plainText(reason, 'El motivo', LIMITS.rejectionReasonMax, {
      min: LIMITS.rejectionReasonMin,
      required: true,
      multiLine: true,
    })
    setFieldError(invalid)
    if (invalid) return
    try {
      await rejectMutation.mutateAsync({ id: companyId, body: { reason: multiLine(reason) ?? '' } })
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
      <FormField
        id="reject-reason"
        label="Motivo"
        error={fieldError ?? undefined}
        counter={{ length: reason.trim().length, max: LIMITS.rejectionReasonMax }}
      >
        <Textarea
          {...fieldAria('reject-reason', fieldError ?? undefined)}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={3}
        />
      </FormField>
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
