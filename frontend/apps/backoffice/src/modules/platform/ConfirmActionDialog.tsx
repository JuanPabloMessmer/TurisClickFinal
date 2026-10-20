import type { ReactNode } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

/**
 * Confirmación de una sanción del administrador (suspender o reactivar contenido o cuentas).
 *
 * Explica el efecto antes de pedir el clic, porque es lo que el administrador no ve desde la tabla. Mientras la
 * operación viaja no se puede cerrar ni confirmar otra vez, y si el servidor la rechaza el motivo queda adentro
 * del diálogo, junto a la acción que falló.
 */
export function ConfirmActionDialog({
  open,
  title,
  children,
  confirmLabel,
  destructive = false,
  pending,
  error,
  onConfirm,
  onClose,
}: {
  open: boolean
  title: string
  children: ReactNode
  confirmLabel: string
  destructive?: boolean
  pending: boolean
  error: string | null
  onConfirm: () => void
  onClose: () => void
}) {
  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && !pending) onClose()
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription asChild>
            <div className="flex flex-col gap-2 text-body text-ink-muted">{children}</div>
          </DialogDescription>
        </DialogHeader>
        {error && <Alert variant="destructive">{error}</Alert>}
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={pending}>
            Cancelar
          </Button>
          <Button variant={destructive ? 'destructive' : 'default'} loading={pending} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
