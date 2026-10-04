import { formatCurrency } from '@turisclick/utils'
import { Calendar, Compass, Eye, EyeOff, Pencil, Plus, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { cn } from '@/lib/utils'
import { useMyExperiences, usePublishExperience, useUnpublishExperience } from './api'

const statusVariant: Record<string, 'success' | 'warning' | 'neutral'> = {
  PUBLISHED: 'success',
  DRAFT: 'warning',
  UNPUBLISHED: 'neutral',
}

const statusLabel: Record<string, string> = {
  PUBLISHED: 'Publicada',
  DRAFT: 'Borrador',
  UNPUBLISHED: 'Pausada',
}

type Filter = 'ALL' | 'PUBLISHED' | 'DRAFT'

const filters: { value: Filter; label: string }[] = [
  { value: 'ALL', label: 'Todas' },
  { value: 'PUBLISHED', label: 'Publicadas' },
  { value: 'DRAFT', label: 'Sin publicar' },
]

export function ExperiencesPage() {
  const { data, isLoading } = useMyExperiences()
  const publishMutation = usePublishExperience()
  const unpublishMutation = useUnpublishExperience()
  const [actionError, setActionError] = useState<string | null>(null)
  const [pendingId, setPendingId] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<Filter>('ALL')

  const experiences = useMemo(() => data?.items ?? [], [data])

  // Búsqueda y filtro en memoria: el endpoint devuelve la lista completa del operador y agregar
  // parámetros nuevos seria cambiar el contrato de la API, que esta fase no toca.
  const visible = useMemo(() => {
    const term = search.trim().toLowerCase()
    return experiences.filter((experience) => {
      const matchesTerm =
        term.length === 0 ||
        (experience.title ?? '').toLowerCase().includes(term) ||
        (experience.destinationName ?? '').toLowerCase().includes(term)
      const matchesFilter =
        filter === 'ALL' || (filter === 'PUBLISHED' ? experience.status === 'PUBLISHED' : experience.status !== 'PUBLISHED')
      return matchesTerm && matchesFilter
    })
  }, [experiences, search, filter])

  const publishedCount = experiences.filter((experience) => experience.status === 'PUBLISHED').length

  const onTogglePublish = async (id: string, status: string | null | undefined) => {
    setActionError(null)
    setPendingId(id)
    try {
      if (status === 'PUBLISHED') {
        await unpublishMutation.mutateAsync(id)
      } else {
        await publishMutation.mutateAsync(id)
      }
    } catch (error) {
      setActionError(getErrorMessage(error))
    } finally {
      setPendingId(null)
    }
  }

  return (
    <div>
      <PageHeader
        title="Experiencias"
        description="Lo que vendés. Una experiencia necesita fechas futuras con cupo para poder publicarse."
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

      <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex gap-1 rounded-sm bg-muted p-1" role="group" aria-label="Filtrar por estado">
          {filters.map((option) => (
            <button
              key={option.value}
              type="button"
              onClick={() => setFilter(option.value)}
              aria-pressed={filter === option.value}
              className={cn(
                'h-9 rounded-sm px-3 text-label font-medium text-ink-muted transition-colors hover:text-foreground',
                filter === option.value && 'bg-surface text-foreground shadow-sm',
              )}
            >
              {option.label}
            </button>
          ))}
        </div>

        <div className="relative sm:w-72">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-muted" aria-hidden="true" />
          <Input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Buscar por título o destino"
            aria-label="Buscar experiencias"
            className="pl-9"
          />
        </div>
      </div>

      <Card>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="min-w-56">Experiencia</TableHead>
              <TableHead className="min-w-32">Destino</TableHead>
              <TableHead className="min-w-24">Precio</TableHead>
              <TableHead className="min-w-28">Estado</TableHead>
              <TableHead className="min-w-64 text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={5} />}
            {!isLoading && experiences.length === 0 && (
              <TableEmptyRow
                colSpan={5}
                icon={Compass}
                title="Todavía no creaste ninguna experiencia"
                description="Creá la primera, cargale fechas con cupo y publicala para que aparezca en la app."
              />
            )}
            {!isLoading && experiences.length > 0 && visible.length === 0 && (
              <TableEmptyRow
                colSpan={5}
                icon={Search}
                title="Ninguna experiencia coincide"
                description="Probá con otro texto o volvé a “Todas”."
              />
            )}
            {visible.map((experience) => {
              const isPublished = experience.status === 'PUBLISHED'
              return (
                <TableRow key={experience.id}>
                  <TableCell>
                    <Link
                      to={`/provider/experiences/${experience.id}/edit`}
                      className="text-body font-medium text-foreground hover:text-primary hover:underline"
                    >
                      {experience.title}
                    </Link>
                  </TableCell>
                  <TableCell className="text-ink-muted">{experience.destinationName}</TableCell>
                  <TableCell className="font-medium">
                    {formatCurrency(experience.price ?? 0, experience.currency ?? 'BOB')}
                  </TableCell>
                  <TableCell>
                    <Badge variant={statusVariant[experience.status ?? ''] ?? 'neutral'}>
                      {statusLabel[experience.status ?? ''] ?? experience.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    <div className="flex flex-wrap items-center justify-end gap-1">
                      <Button variant="ghost" size="sm" asChild>
                        <Link to={`/provider/experiences/${experience.id}/edit`}>
                          <Pencil className="h-4 w-4" aria-hidden="true" />
                          Editar
                        </Link>
                      </Button>
                      <Button variant="ghost" size="sm" asChild>
                        <Link to={`/provider/experiences/${experience.id}/availability`}>
                          <Calendar className="h-4 w-4" aria-hidden="true" />
                          Fechas
                        </Link>
                      </Button>
                      {/* La acción que cambia lo que ve el público va aparte y en outline: antes era el
                          botón de más peso visual de la fila, al lado de dos navegaciones inocuas. */}
                      <Button
                        size="sm"
                        variant="outline"
                        loading={pendingId === experience.id}
                        onClick={() => void onTogglePublish(experience.id!, experience.status)}
                      >
                        {isPublished ? (
                          <EyeOff className="h-4 w-4" aria-hidden="true" />
                        ) : (
                          <Eye className="h-4 w-4" aria-hidden="true" />
                        )}
                        {isPublished ? 'Despublicar' : 'Publicar'}
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </Card>

      {!isLoading && experiences.length > 0 && (
        <p className="mt-3 text-label text-ink-muted">
          {visible.length} de {experiences.length}{' '}
          {experiences.length === 1 ? 'experiencia' : 'experiencias'} · {publishedCount} publicada
          {publishedCount === 1 ? '' : 's'}
        </p>
      )}
    </div>
  )
}
