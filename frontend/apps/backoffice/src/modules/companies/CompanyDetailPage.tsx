import { formatDateTime } from '@turisclick/utils'
import { ArrowLeft, Check, X } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { getErrorMessage } from '@/lib/errors'
import { RejectDialog } from './RejectDialog'
import { useApproveCompany, useCompany } from './api'
import { statusLabel, statusVariant } from './CompaniesApprovalPage'

export function CompanyDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { data: company, isLoading } = useCompany(id)
  const approveMutation = useApproveCompany()
  const [actionError, setActionError] = useState<string | null>(null)
  const [rejecting, setRejecting] = useState(false)

  if (isLoading || !company) return <FullScreenSpinner />

  const onApprove = async () => {
    setActionError(null)
    try {
      await approveMutation.mutateAsync(id!)
    } catch (error) {
      setActionError(getErrorMessage(error))
    }
  }

  return (
    <div className="max-w-2xl">
      <Link
        to="/admin/companies"
        className="mb-4 inline-flex items-center gap-1.5 text-sm font-medium text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        Volver a Empresas
      </Link>

      <div className="mb-6 flex items-center justify-between">
        <PageHeader title={company.name ?? 'Empresa'} description="UC-A-01." />
        <Badge variant={statusVariant[company.status ?? ''] ?? 'neutral'}>{statusLabel[company.status ?? ''] ?? company.status}</Badge>
      </div>

      {actionError && (
        <Alert variant="destructive" className="mb-4">
          {actionError}
        </Alert>
      )}

      <Card className="mb-6">
        <CardContent className="grid grid-cols-1 gap-5 pt-6 text-sm sm:grid-cols-2">
          <Row label="Documento legal" value={company.legalDocument} />
          <Row label="Email de contacto" value={company.contactEmail} />
          <Row label="Teléfono de contacto" value={company.contactPhone} />
          <Row label="Creada" value={company.createdAt ? formatDateTime(company.createdAt) : undefined} />
          <Row label="Aprobada" value={company.approvedAt ? formatDateTime(company.approvedAt) : undefined} />
          <div className="sm:col-span-2">
            <p className="text-muted-foreground">Descripción</p>
            <p className="font-medium text-foreground">{company.description ?? '—'}</p>
          </div>
        </CardContent>
      </Card>

      {company.status === 'REJECTED' && company.rejectionReason && (
        <Alert variant="destructive" className="mb-6">
          <span className="font-medium">Motivo del rechazo:</span> {company.rejectionReason}
        </Alert>
      )}

      {company.status === 'PENDING_APPROVAL' && (
        <div className="flex gap-2">
          <Button onClick={() => void onApprove()}>
            <Check className="h-3.5 w-3.5" aria-hidden="true" />
            Aprobar
          </Button>
          <Button variant="destructive" onClick={() => setRejecting(true)}>
            <X className="h-3.5 w-3.5" aria-hidden="true" />
            Rechazar
          </Button>
        </div>
      )}

      {(company.status === 'APPROVED' || company.status === 'SUSPENDED') && (
        <Alert variant="info">
          {company.status === 'APPROVED'
            ? 'Suspender una empresa (UC-A-08) todavía no está implementado — está planificado para una oleada futura.'
            : 'Reactivar una empresa suspendida no tiene un caso de uso definido todavía en docs/use-cases.md.'}
        </Alert>
      )}

      <RejectDialog companyId={rejecting ? (id ?? null) : null} onClose={() => setRejecting(false)} />
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
