import type { CompanyResponse } from '@turisclick/api-client'
import { Building2, Check, X } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'
import { getErrorMessage } from '@/lib/errors'
import { useApproveCompany, useCompanies, useRejectCompany } from './api'

const statusVariant: Record<string, 'success' | 'warning' | 'destructive' | 'neutral'> = {
  APPROVED: 'success',
  PENDING_APPROVAL: 'warning',
  REJECTED: 'destructive',
  SUSPENDED: 'destructive',
}

const statusLabel: Record<string, string> = {
  APPROVED: 'Aprobada',
  PENDING_APPROVAL: 'Pendiente',
  REJECTED: 'Rechazada',
  SUSPENDED: 'Suspendida',
}

export function CompaniesApprovalPage() {
  const [statusFilter, setStatusFilter] = useState<string>('PENDING_APPROVAL')
  const { data, isLoading } = useCompanies(statusFilter === 'ALL' ? undefined : statusFilter)
  const approveMutation = useApproveCompany()
  const [actionError, setActionError] = useState<string | null>(null)
  const [rejecting, setRejecting] = useState<CompanyResponse | null>(null)

  const onApprove = async (id: string) => {
    setActionError(null)
    try {
      await approveMutation.mutateAsync(id)
    } catch (error) {
      setActionError(getErrorMessage(error))
    }
  }

  const companies = data?.items ?? []

  return (
    <div>
      <PageHeader title="Empresas" description="UC-A-01/02/03 — revisar, aprobar y rechazar solicitudes de Provider." />

      <div className="mb-4 flex items-center gap-2">
        <Label className="shrink-0">Estado</Label>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-56">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="PENDING_APPROVAL">Pendientes de aprobación</SelectItem>
            <SelectItem value="APPROVED">Aprobadas</SelectItem>
            <SelectItem value="REJECTED">Rechazadas</SelectItem>
            <SelectItem value="SUSPENDED">Suspendidas</SelectItem>
            <SelectItem value="ALL">Todas</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {actionError && (
        <Alert variant="destructive" className="mb-4">
          {actionError}
        </Alert>
      )}

      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Empresa</TableHead>
              <TableHead>Documento legal</TableHead>
              <TableHead>Contacto</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={5} />}
            {!isLoading && companies.length === 0 && (
              <TableEmptyRow colSpan={5} icon={Building2} title="No hay empresas en este estado" />
            )}
            {companies.map((company) => (
              <TableRow key={company.id}>
                <TableCell className="font-medium">{company.name}</TableCell>
                <TableCell className="text-muted-foreground">{company.legalDocument}</TableCell>
                <TableCell className="text-muted-foreground">{company.contactEmail}</TableCell>
                <TableCell>
                  <Badge variant={statusVariant[company.status ?? ''] ?? 'neutral'}>
                    {statusLabel[company.status ?? ''] ?? company.status}
                  </Badge>
                </TableCell>
                <TableCell className="text-right">
                  {company.status === 'PENDING_APPROVAL' && (
                    <div className="flex justify-end gap-2">
                      <Button size="sm" onClick={() => void onApprove(company.id!)}>
                        <Check className="h-3.5 w-3.5" aria-hidden="true" />
                        Aprobar
                      </Button>
                      <Button variant="destructive" size="sm" onClick={() => setRejecting(company)}>
                        <X className="h-3.5 w-3.5" aria-hidden="true" />
                        Rechazar
                      </Button>
                    </div>
                  )}
                  {company.status === 'REJECTED' && company.rejectionReason && (
                    <span className="text-xs text-muted-foreground">Motivo: {company.rejectionReason}</span>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      <RejectDialog company={rejecting} onClose={() => setRejecting(null)} />
    </div>
  )
}

function RejectDialog({ company, onClose }: { company: CompanyResponse | null; onClose: () => void }) {
  return (
    <Dialog
      open={!!company}
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      {company && <RejectDialogContent key={company.id} company={company} onClose={onClose} />}
    </Dialog>
  )
}

function RejectDialogContent({ company, onClose }: { company: CompanyResponse; onClose: () => void }) {
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
      await rejectMutation.mutateAsync({ id: company.id!, body: { reason } })
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    }
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Rechazar solicitud de {company.name}</DialogTitle>
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
