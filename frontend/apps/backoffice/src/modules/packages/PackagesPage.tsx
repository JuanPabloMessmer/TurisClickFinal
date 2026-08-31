import { formatCurrency } from '@turisclick/utils'
import { Calendar, MapPinned, Package, Pencil, Plus, Radio } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { useMyPackages, usePublishPackage, useUnpublishPackage } from './api'

const statusVariant: Record<string, 'success' | 'warning' | 'neutral'> = {
  PUBLISHED: 'success',
  DRAFT: 'warning',
  UNPUBLISHED: 'neutral',
}

const statusLabel: Record<string, string> = {
  PUBLISHED: 'Publicado',
  DRAFT: 'Borrador',
  UNPUBLISHED: 'Despublicado',
}

export function PackagesPage() {
  const { data, isLoading } = useMyPackages()
  const publishMutation = usePublishPackage()
  const unpublishMutation = useUnpublishPackage()
  const [actionError, setActionError] = useState<string | null>(null)

  const packages = data?.items ?? []

  const onTogglePublish = async (id: string, status: string | null | undefined) => {
    setActionError(null)
    try {
      if (status === 'PUBLISHED') {
        await unpublishMutation.mutateAsync(id)
      } else {
        await publishMutation.mutateAsync(id)
      }
    } catch (error) {
      setActionError(getErrorMessage(error))
    }
  }

  return (
    <div>
      <PageHeader
        title="Paquetes"
        description="UC-P-07/08/09 — paquetes multi-día de tu empresa, combinando experiencias propias e ítems descriptivos."
        actions={
          <Button asChild>
            <Link to="/provider/packages/new">
              <Plus className="h-4 w-4" aria-hidden="true" />
              Nuevo paquete
            </Link>
          </Button>
        }
      />

      {actionError && (
        <Alert variant="destructive" className="mb-4">
          {actionError}
        </Alert>
      )}

      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Título</TableHead>
              <TableHead>Destino</TableHead>
              <TableHead>Duración</TableHead>
              <TableHead>Precio</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={6} />}
            {!isLoading && packages.length === 0 && (
              <TableEmptyRow
                colSpan={6}
                icon={Package}
                title="Todavía no creaste ningún paquete"
                description="Usá 'Nuevo paquete' para armar tu primer itinerario multi-día."
              />
            )}
            {packages.map((pkg) => (
              <TableRow key={pkg.id}>
                <TableCell className="font-medium">{pkg.title}</TableCell>
                <TableCell className="text-muted-foreground">{pkg.destinationName}</TableCell>
                <TableCell>{pkg.durationDays} días</TableCell>
                <TableCell>{formatCurrency(pkg.price ?? 0, pkg.currency ?? 'USD')}</TableCell>
                <TableCell>
                  <Badge variant={statusVariant[pkg.status ?? ''] ?? 'neutral'}>{statusLabel[pkg.status ?? ''] ?? pkg.status}</Badge>
                </TableCell>
                <TableCell className="text-right">
                  <div className="flex flex-wrap justify-end gap-2">
                    <Button variant="outline" size="sm" asChild>
                      <Link to={`/provider/packages/${pkg.id}/edit`}>
                        <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                        Editar
                      </Link>
                    </Button>
                    <Button variant="outline" size="sm" asChild>
                      <Link to={`/provider/packages/${pkg.id}/availability`}>
                        <Calendar className="h-3.5 w-3.5" aria-hidden="true" />
                        Disponibilidad
                      </Link>
                    </Button>
                    <Button
                      size="sm"
                      variant={pkg.status === 'PUBLISHED' ? 'outline' : 'default'}
                      onClick={() => void onTogglePublish(pkg.id!, pkg.status)}
                    >
                      <Radio className="h-3.5 w-3.5" aria-hidden="true" />
                      {pkg.status === 'PUBLISHED' ? 'Despublicar' : 'Publicar'}
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>

      {packages.length > 0 && (
        <p className="mt-4 flex items-center gap-1.5 text-xs text-muted-foreground">
          <MapPinned className="h-3.5 w-3.5" aria-hidden="true" />
          Publicar exige al menos un ítem y una disponibilidad futura con cupo (UC-P-09).
        </p>
      )}
    </div>
  )
}
