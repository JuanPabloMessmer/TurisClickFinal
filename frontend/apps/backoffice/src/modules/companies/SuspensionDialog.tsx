import { useRef, useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { getErrorMessage } from '@/lib/errors'
import { useReactivateCompany, useSuspendCompany } from './api'

export type SuspensionAction = 'suspend' | 'reactivate'

/**
 * UC-A-08. Suspender o reactivar una empresa, siempre con confirmación: no borra nada, pero deja al catálogo entero
 * fuera de circulación de un saque y conviene que quien lo hace sepa exactamente qué pasa.
 */
export function SuspensionDialog({
  companyId,
  companyName,
  action,
  onClose,
}: {
  companyId: string
  companyName?: string | null
  action: SuspensionAction | null
  onClose: () => void
}) {
  return (
    <Dialog
      open={!!action}
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      {action && (
        <SuspensionDialogContent key={action} companyId={companyId} companyName={companyName} action={action} onClose={onClose} />
      )}
    </Dialog>
  )
}

function SuspensionDialogContent({
  companyId,
  companyName,
  action,
  onClose,
}: {
  companyId: string
  companyName?: string | null
  action: SuspensionAction
  onClose: () => void
}) {
  const suspendMutation = useSuspendCompany()
  const reactivateMutation = useReactivateCompany()
  const mutation = action === 'suspend' ? suspendMutation : reactivateMutation
  const [error, setError] = useState<string | null>(null)
  // isPending recién se refleja en el próximo render: el ref corta el doble clic que llega antes.
  const submitting = useRef(false)

  const onConfirm = async () => {
    if (submitting.current) return
    submitting.current = true
    setError(null)
    try {
      await mutation.mutateAsync(companyId)
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    } finally {
      submitting.current = false
    }
  }

  const name = companyName ? ` ${companyName}` : ''
  const isSuspend = action === 'suspend'

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{isSuspend ? `¿Suspender${name}?` : `¿Reactivar${name}?`}</DialogTitle>
        <DialogDescription>
          {isSuspend
            ? 'Todas sus experiencias y paquetes dejan de verse para los viajeros y para el asistente, y no se pueden hacer reservas nuevas. Las reservas que ya existen se mantienen. Su equipo no va a poder operar hasta que la reactives. No se borra nada.'
            : 'Su catálogo vuelve a circular: cada experiencia y paquete recupera el estado que tenía antes de la suspensión, y su equipo puede volver a operar.'}
        </DialogDescription>
      </DialogHeader>
      {error && (
        <Alert variant="destructive" className="mt-2">
          {error}
        </Alert>
      )}
      <DialogFooter>
        <Button variant="outline" onClick={onClose} disabled={mutation.isPending}>
          Cancelar
        </Button>
        <Button
          variant={isSuspend ? 'destructive' : 'default'}
          onClick={() => void onConfirm()}
          loading={mutation.isPending}
        >
          {isSuspend ? 'Suspender' : 'Reactivar'}
        </Button>
      </DialogFooter>
    </DialogContent>
  )
}
