import { Building2, Check, Search, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { RejectDialog } from './RejectDialog'
import { useApproveCompany, useCompanies } from './api'

export const statusVariant: Record<string, 'success' | 'warning' | 'destructive' | 'neutral'> = {
  APPROVED: 'success',
  PENDING_APPROVAL: 'warning',
  REJECTED: 'destructive',
  SUSPENDED: 'destructive',
}

export const statusLabel: Record<string, string> = {
  APPROVED: 'Aprobada',
  PENDING_APPROVAL: 'Pendiente',
  REJECTED: 'Rechazada',
  SUSPENDED: 'Suspendida',
}

const PAGE_SIZE = 20

export function CompaniesApprovalPage() {
  // Filtros en la URL: refrescar la página (o volver con el botón atrás) conserva estado/búsqueda/página.
  const [searchParams, setSearchParams] = useSearchParams()
  const statusFilter = searchParams.get('status') ?? 'ALL'
  const page = Number(searchParams.get('page') ?? '1')
  const urlSearch = searchParams.get('search') ?? ''

  const [searchInput, setSearchInput] = useState(urlSearch)

  // Debounce: escribir en la URL (y por lo tanto disparar la query) 350ms después de la última tecla.
  useEffect(() => {
    const handle = setTimeout(() => {
      if (searchInput === urlSearch) return
      setSearchParams((prev) => {
        const next = new URLSearchParams(prev)
        if (searchInput) next.set('search', searchInput)
        else next.delete('search')
        next.set('page', '1')
        return next
      })
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, 350)
    return () => clearTimeout(handle)
  }, [searchInput])

  const { data, isLoading, isPlaceholderData } = useCompanies({
    status: statusFilter === 'ALL' ? undefined : statusFilter,
    search: urlSearch || undefined,
    page,
    pageSize: PAGE_SIZE,
  })
  const approveMutation = useApproveCompany()
  const [actionError, setActionError] = useState<string | null>(null)
  const [rejectingId, setRejectingId] = useState<string | null>(null)

  const onApprove = async (id: string) => {
    setActionError(null)
    try {
      await approveMutation.mutateAsync(id)
    } catch (error) {
      setActionError(getErrorMessage(error))
    }
  }

  const setStatusFilter = (status: string) =>
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev)
      next.set('status', status)
      next.set('page', '1')
      return next
    })

  const setPage = (nextPage: number) =>
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev)
      next.set('page', String(nextPage))
      return next
    })

  const companies = data?.items ?? []
  const totalPages = data?.totalPages ?? 1
  const totalCount = data?.totalCount ?? 0

  return (
    <div>
      <PageHeader title="Empresas" description="UC-A-01/02/03 — revisar, aprobar y rechazar solicitudes de Provider." />

      <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="relative flex-1 sm:max-w-sm">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Buscar por nombre, NIT o email…"
            className="pl-9"
          />
        </div>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="sm:w-56">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="ALL">Todas</SelectItem>
            <SelectItem value="PENDING_APPROVAL">Pendientes</SelectItem>
            <SelectItem value="APPROVED">Aprobadas</SelectItem>
            <SelectItem value="REJECTED">Rechazadas</SelectItem>
            <SelectItem value="SUSPENDED">Suspendidas</SelectItem>
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
              <TableEmptyRow
                colSpan={5}
                icon={Building2}
                title="No hay empresas que coincidan"
                description={urlSearch || statusFilter !== 'ALL' ? 'Probá ajustar la búsqueda o el filtro de estado.' : undefined}
              />
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
                  <div className="flex flex-wrap justify-end gap-2">
                    {company.status === 'PENDING_APPROVAL' && (
                      <>
                        <Button size="sm" onClick={() => void onApprove(company.id!)}>
                          <Check className="h-3.5 w-3.5" aria-hidden="true" />
                          Aprobar
                        </Button>
                        <Button variant="destructive" size="sm" onClick={() => setRejectingId(company.id!)}>
                          <X className="h-3.5 w-3.5" aria-hidden="true" />
                          Rechazar
                        </Button>
                      </>
                    )}
                    <Button variant="outline" size="sm" asChild>
                      <Link to={`/admin/companies/${company.id}`}>Ver detalle</Link>
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      {totalCount > 0 && (
        <div className="mt-4 flex items-center justify-between text-sm text-muted-foreground">
          <span>
            Página {page} de {totalPages} · {totalCount} {totalCount === 1 ? 'empresa' : 'empresas'}
          </span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              Anterior
            </Button>
            <Button variant="outline" size="sm" disabled={page >= totalPages || isPlaceholderData} onClick={() => setPage(page + 1)}>
              Siguiente
            </Button>
          </div>
        </div>
      )}

      <RejectDialog companyId={rejectingId} onClose={() => setRejectingId(null)} />
    </div>
  )
}
