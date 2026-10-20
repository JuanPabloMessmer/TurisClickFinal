import { formatCurrency } from '@turisclick/utils'
import { Plane, Search } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableEmptyRow,
  TableHead,
  TableHeader,
  TableLoadingRow,
  TableRow,
} from '@/components/ui/table'
import { getErrorMessage } from '@/lib/errors'
import { type ContentKind, type ModerationAction, useAdminExperiences, useAdminPackages, useModerateContent } from './api'
import { ConfirmActionDialog } from './ConfirmActionDialog'

/**
 * El catálogo completo de la plataforma, de todas las empresas.
 *
 * Qué muestra que el listado del operador no puede mostrar: de quién es cada producto, en qué estado de
 * publicación está —incluidos borradores y suspendidos, que son justamente los que la moderación necesita
 * ver— y si tiene lo que hace falta para venderse (salidas futuras, categorías, política de cancelación).
 *
 * Es también donde se modera (UC-A-07): cada fila se puede suspender, y una suspendida se puede liberar.
 */

const PAGE_SIZE = 20

const PUBLICATION_LABEL: Record<string, string> = {
  PUBLISHED: 'Publicado',
  DRAFT: 'Borrador',
  // Despublicado: el operador lo sacó del catálogo. Distinto de suspendido, que lo decide la plataforma.
  UNPUBLISHED: 'Despublicado',
  SUSPENDED: 'Suspendido',
}

const PUBLICATION_VARIANT: Record<string, 'success' | 'warning' | 'destructive' | 'neutral'> = {
  PUBLISHED: 'success',
  DRAFT: 'neutral',
  UNPUBLISHED: 'warning',
  SUSPENDED: 'destructive',
}

/** Filtros en la URL: volver con el botón atrás conserva lo que se estaba mirando. */
function useCatalogFilters() {
  const [searchParams, setSearchParams] = useSearchParams()
  const status = searchParams.get('status') ?? 'ALL'
  const flight = searchParams.get('flight') ?? 'ALL'
  const page = Math.max(Number(searchParams.get('page') ?? '1'), 1)
  const urlSearch = searchParams.get('search') ?? ''
  const [searchInput, setSearchInput] = useState(urlSearch)

  useEffect(() => {
    const handle = setTimeout(() => {
      if (searchInput === urlSearch) return
      setSearchParams((previous) => {
        const next = new URLSearchParams(previous)
        if (searchInput) next.set('search', searchInput)
        else next.delete('search')
        next.set('page', '1')
        return next
      })
    }, 350)
    return () => clearTimeout(handle)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchInput])

  const update = (key: string, value: string) =>
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous)
      if (value === 'ALL') next.delete(key)
      else next.set(key, value)
      next.set('page', '1')
      return next
    })

  const goToPage = (value: number) =>
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous)
      next.set('page', String(value))
      return next
    })

  return { status, flight, page, urlSearch, searchInput, setSearchInput, update, goToPage }
}

function StatusFilter({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger className="w-full sm:w-52" aria-label="Filtrar por estado">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="ALL">Todos los estados</SelectItem>
        <SelectItem value="PUBLISHED">Publicados</SelectItem>
        <SelectItem value="DRAFT">Borradores</SelectItem>
        <SelectItem value="UNPUBLISHED">Despublicados</SelectItem>
        <SelectItem value="SUSPENDED">Suspendidos</SelectItem>
      </SelectContent>
    </Select>
  )
}

function SearchField({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <div className="relative w-full sm:max-w-sm">
      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-muted" aria-hidden="true" />
      <Input
        className="pl-9"
        placeholder="Buscar por título o empresa"
        aria-label="Buscar en el catálogo"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </div>
  )
}

const NOUN: Record<ContentKind, { label: string; female: boolean }> = {
  experience: { label: 'la experiencia', female: true },
  package: { label: 'el paquete', female: false },
}

/**
 * La suspensión y su reverso, con su diálogo de confirmación. Un producto suspendido se libera; cualquier otro
 * (publicado, borrador o despublicado) se puede suspender.
 */
function useModeration(kind: ContentKind) {
  const mutation = useModerateContent(kind)
  const [target, setTarget] = useState<{ id: string; title: string; action: ModerationAction } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const inFlight = useRef(false)
  const noun = NOUN[kind]
  const ending = noun.female ? 'a' : 'o'

  const ask = (row: { id?: string; title?: string | null; status?: string | null }) => {
    if (!row.id) return
    mutation.reset()
    setError(null)
    setNotice(null)
    setTarget({ id: row.id, title: row.title ?? '', action: row.status === 'SUSPENDED' ? 'restore' : 'suspend' })
  }

  const confirm = () => {
    // `isPending` recién cambia cuando la mutación arranca, que es asíncrono: un doble clic llega antes. La
    // referencia corta el segundo envío en el acto; el botón bloqueado es lo que se ve.
    if (!target || inFlight.current) return
    inFlight.current = true
    mutation.mutate(
      { id: target.id, action: target.action },
      {
        onSettled: () => {
          inFlight.current = false
        },
        onSuccess: () => {
          setNotice(
            target.action === 'suspend'
              ? `«${target.title}» quedó suspendid${ending}.`
              : `Se levantó la suspensión de «${target.title}». Queda despublicad${ending} hasta que la empresa l${ending} vuelva a publicar.`,
          )
          setTarget(null)
        },
        onError: (mutationError) => setError(getErrorMessage(mutationError)),
      },
    )
  }

  const action = (row: { id?: string; title?: string | null; status?: string | null }) => {
    const suspended = row.status === 'SUSPENDED'
    return (
      <Button
        variant={suspended ? 'outline' : 'ghost'}
        size="sm"
        disabled={mutation.isPending}
        onClick={() => ask(row)}
        aria-label={`${suspended ? 'Levantar suspensión de' : 'Suspender'} ${row.title ?? ''}`.trim()}
      >
        {suspended ? 'Levantar suspensión' : 'Suspender'}
      </Button>
    )
  }

  const dialog = (
    <ConfirmActionDialog
      open={target !== null}
      title={target?.action === 'restore' ? `¿Levantar la suspensión de ${noun.label}?` : `¿Suspender ${noun.label}?`}
      confirmLabel={target?.action === 'restore' ? 'Levantar suspensión' : 'Suspender'}
      destructive={target?.action === 'suspend'}
      pending={mutation.isPending}
      error={error}
      onConfirm={confirm}
      onClose={() => setTarget(null)}
    >
      {target?.action === 'restore' ? (
        <>
          <p>
            «{target.title}» vuelve a manos de la empresa, pero no se publica sol{ending}: queda despublicad{ending}.
          </p>
          <p>Para que los viajeros l{ending} vuelvan a ver, la empresa tiene que publicarl{ending} de nuevo.</p>
        </>
      ) : (
        <>
          <p>
            «{target?.title}» deja de verse en el catálogo de la app y el asistente deja de recomendarl{ending}.
          </p>
          <p>Las reservas que ya existen no se cancelan. Sólo un administrador puede levantar la suspensión.</p>
        </>
      )}
    </ConfirmActionDialog>
  )

  const banner = notice && <Alert variant="success">{notice}</Alert>

  return { action, dialog, banner }
}

function Pagination({
  page,
  totalPages,
  onChange,
}: {
  page: number
  totalPages: number
  onChange: (page: number) => void
}) {
  if (totalPages <= 1) return null

  return (
    <div className="flex items-center justify-between gap-3 border-t border-border px-4 py-3">
      <p className="text-label text-ink-muted">
        Página {page} de {totalPages}
      </p>
      <div className="flex gap-2">
        <Button variant="outline" disabled={page <= 1} onClick={() => onChange(page - 1)}>
          Anterior
        </Button>
        <Button variant="outline" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
          Siguiente
        </Button>
      </div>
    </div>
  )
}

export function AdminExperiencesPage() {
  const filters = useCatalogFilters()
  const moderation = useModeration('experience')

  const { data, isLoading } = useAdminExperiences({
    status: filters.status === 'ALL' ? undefined : filters.status,
    search: filters.urlSearch || undefined,
    page: filters.page,
    pageSize: PAGE_SIZE,
  })

  const items = data?.items ?? []
  const totalPages = data?.totalPages ?? 1

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Experiencias"
        description="Las experiencias de todas las empresas, publicadas o no, con su destino y su precio. Desde acá suspendés las que incumplen las políticas."
      />

      {moderation.banner}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <SearchField value={filters.searchInput} onChange={filters.setSearchInput} />
        <StatusFilter value={filters.status} onChange={(value) => filters.update('status', value)} />
      </div>

      <Card className="overflow-hidden p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Experiencia</TableHead>
              <TableHead>Empresa</TableHead>
              <TableHead>Destino</TableHead>
              <TableHead className="text-right">Precio</TableHead>
              <TableHead className="text-right">Fechas futuras</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>
                <span className="sr-only">Acciones</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading ? (
              <TableLoadingRow colSpan={7} />
            ) : items.length === 0 ? (
              <TableEmptyRow colSpan={7} title="No hay experiencias que coincidan" description="Probá con otro estado o con otra búsqueda." />
            ) : (
              items.map((row) => (
                <TableRow key={row.id}>
                  <TableCell className="font-medium text-foreground">{row.title}</TableCell>
                  <TableCell>
                    <span className="block">{row.companyName}</span>
                    {row.companyStatus !== 'APPROVED' && (
                      <span className="text-caption text-ink-muted">Empresa {row.companyStatus?.toLowerCase()}</span>
                    )}
                  </TableCell>
                  <TableCell>{row.destinationName}</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {formatCurrency(row.price ?? 0, row.currency ?? 'BOB')}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {/* Sin fechas futuras no se puede reservar, aunque esté publicada. */}
                    {row.futureAvailabilities === 0 ? (
                      <span className="text-warning-fg">0</span>
                    ) : (
                      row.futureAvailabilities
                    )}
                  </TableCell>
                  <TableCell>
                    <Badge variant={PUBLICATION_VARIANT[row.status ?? ''] ?? 'neutral'}>
                      {PUBLICATION_LABEL[row.status ?? ''] ?? row.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">{moderation.action(row)}</TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
        <Pagination page={filters.page} totalPages={totalPages} onChange={filters.goToPage} />
      </Card>

      {moderation.dialog}
    </div>
  )
}

export function AdminPackagesPage() {
  const filters = useCatalogFilters()
  const moderation = useModeration('package')

  const { data, isLoading } = useAdminPackages({
    status: filters.status === 'ALL' ? undefined : filters.status,
    search: filters.urlSearch || undefined,
    withFlight: filters.flight === 'ALL' ? undefined : filters.flight === 'YES',
    page: filters.page,
    pageSize: PAGE_SIZE,
  })

  const items = data?.items ?? []
  const totalPages = data?.totalPages ?? 1

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Paquetes"
        description="Los viajes de varios días de todas las empresas, con si incluyen vuelo y si tienen política de cancelación. Desde acá suspendés los que incumplen las políticas."
      />

      {moderation.banner}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <SearchField value={filters.searchInput} onChange={filters.setSearchInput} />
        <StatusFilter value={filters.status} onChange={(value) => filters.update('status', value)} />
        <Select value={filters.flight} onValueChange={(value) => filters.update('flight', value)}>
          <SelectTrigger className="w-full sm:w-48" aria-label="Filtrar por vuelo">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="ALL">Con y sin vuelo</SelectItem>
            <SelectItem value="YES">Sólo con vuelo</SelectItem>
            <SelectItem value="NO">Sólo sin vuelo</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <Card className="overflow-hidden p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Paquete</TableHead>
              <TableHead>Empresa</TableHead>
              <TableHead>Vuelo</TableHead>
              <TableHead className="text-right">Precio</TableHead>
              <TableHead className="text-right">Salidas futuras</TableHead>
              <TableHead>Cancelación</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>
                <span className="sr-only">Acciones</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading ? (
              <TableLoadingRow colSpan={8} />
            ) : items.length === 0 ? (
              <TableEmptyRow colSpan={8} title="No hay paquetes que coincidan" description="Probá con otro estado o con otra búsqueda." />
            ) : (
              items.map((row) => (
                <TableRow key={row.id}>
                  <TableCell>
                    <span className="block font-medium text-foreground">{row.title}</span>
                    <span className="text-caption text-ink-muted">
                      {row.durationDays} {row.durationDays === 1 ? 'día' : 'días'} · {row.destinationName}
                    </span>
                  </TableCell>
                  <TableCell>{row.companyName}</TableCell>
                  <TableCell>
                    {row.includesFlight ? (
                      <span className="flex items-center gap-1.5 text-label">
                        <Plane className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
                        {row.flightRoute ?? 'Incluye vuelo'}
                      </span>
                    ) : (
                      <span className="text-ink-muted">—</span>
                    )}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {formatCurrency(row.price ?? 0, row.currency ?? 'BOB')}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {row.futureDepartures === 0 ? (
                      <span className="text-warning-fg">0</span>
                    ) : (
                      row.futureDepartures
                    )}
                  </TableCell>
                  <TableCell>
                    {/* Sin política, una reserva confirmada de este paquete no se cancela desde la app. */}
                    {row.hasCancellationPolicy ? (
                      <Badge variant="success">Con política</Badge>
                    ) : (
                      <Badge variant="neutral">Sin política</Badge>
                    )}
                  </TableCell>
                  <TableCell>
                    <Badge variant={PUBLICATION_VARIANT[row.status ?? ''] ?? 'neutral'}>
                      {PUBLICATION_LABEL[row.status ?? ''] ?? row.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">{moderation.action(row)}</TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
        <Pagination page={filters.page} totalPages={totalPages} onChange={filters.goToPage} />
      </Card>

      {moderation.dialog}
    </div>
  )
}
