import { formatCurrency } from '@turisclick/utils'
import { Calendar, Compass, Pencil, Plus, Radio } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { useMyExperiences, usePublishExperience, useUnpublishExperience } from './api'

const statusVariant: Record<string, 'success' | 'warning' | 'neutral'> = {
  PUBLISHED: 'success',
  DRAFT: 'warning',
  UNPUBLISHED: 'neutral',
}

const statusLabel: Record<string, string> = {
  PUBLISHED: 'Publicada',
  DRAFT: 'Borrador',
  UNPUBLISHED: 'Despublicada',
}

export function ExperiencesPage() {
  const { data, isLoading } = useMyExperiences()
  const publishMutation = usePublishExperience()
  const unpublishMutation = useUnpublishExperience()
  const [actionError, setActionError] = useState<string | null>(null)

  const experiences = data?.items ?? []

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
        title="Experiencias"
        description="UC-P-04/05/06 — tus experiencias, en cualquier estado."
        actions={
          <Button asChild>
            <Link to="/provider/experiences/new">
              <Plus className="h-4 w-4" aria-hidden="true" />
              Nueva experiencia
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
              <TableHead>Precio</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={5} />}
            {!isLoading && experiences.length === 0 && (
              <TableEmptyRow
                colSpan={5}
                icon={Compass}
                title="Todavía no creaste ninguna experiencia"
                description="Usá 'Nueva experiencia' para publicar tu primer tour."
              />
            )}
            {experiences.map((experience) => (
              <TableRow key={experience.id}>
                <TableCell className="font-medium">{experience.title}</TableCell>
                <TableCell className="text-muted-foreground">{experience.destinationName}</TableCell>
                <TableCell>{formatCurrency(experience.price ?? 0, experience.currency ?? 'USD')}</TableCell>
                <TableCell>
                  <Badge variant={statusVariant[experience.status ?? ''] ?? 'neutral'}>
                    {statusLabel[experience.status ?? ''] ?? experience.status}
                  </Badge>
                </TableCell>
                <TableCell className="text-right">
                  <div className="flex flex-wrap justify-end gap-2">
                    <Button variant="outline" size="sm" asChild>
                      <Link to={`/provider/experiences/${experience.id}/edit`}>
                        <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                        Editar
                      </Link>
                    </Button>
                    <Button variant="outline" size="sm" asChild>
                      <Link to={`/provider/experiences/${experience.id}/availability`}>
                        <Calendar className="h-3.5 w-3.5" aria-hidden="true" />
                        Disponibilidad
                      </Link>
                    </Button>
                    <Button
                      size="sm"
                      variant={experience.status === 'PUBLISHED' ? 'outline' : 'default'}
                      onClick={() => void onTogglePublish(experience.id!, experience.status)}
                    >
                      <Radio className="h-3.5 w-3.5" aria-hidden="true" />
                      {experience.status === 'PUBLISHED' ? 'Despublicar' : 'Publicar'}
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
    </div>
  )
}
