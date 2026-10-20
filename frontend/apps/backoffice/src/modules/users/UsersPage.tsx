import type { adminApi } from '@turisclick/api-client'
import { Search } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useAuth } from '@/auth/useAuth'
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
import { ConfirmActionDialog } from '@/modules/platform/ConfirmActionDialog'
import { useAdminUsers, useChangeUserStatus } from './api'

/**
 * UC-A-06 — todas las cuentas de la plataforma: viajeros, operadores y administradores.
 *
 * Lo que se hace sobre una cuenta es suspenderla o reactivarla. Suspender bloquea el ingreso y la renovación de
 * sesión; no borra nada. La propia cuenta no se ofrece: el backend igual lo rechaza, y dejarse afuera por un clic
 * no tiene vuelta desde el panel.
 */

const PAGE_SIZE = 20

const ROLE_LABEL: Record<string, string> = {
  ADMIN: 'Administrador',
  PROVIDER: 'Operador',
  TOURIST: 'Viajero',
}

const STATUS_LABEL: Record<string, string> = {
  ACTIVE: 'Activa',
  SUSPENDED: 'Suspendida',
}

type UserRow = adminApi.AdminUserResponse
type Target = { id: string; name: string; email: string; role: string; action: 'suspend' | 'activate' }

/** Filtros en la URL, como en el catálogo: el botón atrás conserva lo que se estaba mirando. */
function useUserFilters() {
  const [searchParams, setSearchParams] = useSearchParams()
  const role = searchParams.get('role') ?? 'ALL'
  const status = searchParams.get('status') ?? 'ALL'
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

  return { role, status, page, urlSearch, searchInput, setSearchInput, update, goToPage }
}

const fullName = (row: UserRow) => `${row.firstName ?? ''} ${row.lastName ?? ''}`.trim() || (row.email ?? '')

export function UsersPage() {
  const { user } = useAuth()
  const filters = useUserFilters()
  const change = useChangeUserStatus()
  const [target, setTarget] = useState<Target | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const inFlight = useRef(false)

  const users = useAdminUsers({
    role: filters.role === 'ALL' ? undefined : (filters.role as adminApi.AdminUserRole),
    status: filters.status === 'ALL' ? undefined : (filters.status as adminApi.AdminUserStatus),
    search: filters.urlSearch || undefined,
    page: filters.page,
    pageSize: PAGE_SIZE,
  })

  const items = users.data?.items ?? []
  const totalPages = users.data?.totalPages ?? 1

  const ask = (row: UserRow) => {
    if (!row.id) return
    change.reset()
    setError(null)
    setNotice(null)
    setTarget({
      id: row.id,
      name: fullName(row),
      email: row.email ?? '',
      role: row.role ?? '',
      action: row.status === 'SUSPENDED' ? 'activate' : 'suspend',
    })
  }

  const confirm = () => {
    // `isPending` recién cambia cuando la mutación arranca, que es asíncrono: un doble clic llega antes. La
    // referencia corta el segundo envío en el acto; el botón bloqueado es lo que se ve.
    if (!target || inFlight.current) return
    inFlight.current = true
    change.mutate(
      { id: target.id, action: target.action },
      {
        onSettled: () => {
          inFlight.current = false
        },
        onSuccess: () => {
          setNotice(
            target.action === 'suspend'
              ? `La cuenta de ${target.name} quedó suspendida.`
              : `La cuenta de ${target.name} volvió a estar activa.`,
          )
          setTarget(null)
        },
        onError: (mutationError) => setError(getErrorMessage(mutationError)),
      },
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Usuarios"
        description="Todas las cuentas de la plataforma: viajeros, operadores y administradores. Desde acá suspendés una cuenta o la volvés a activar."
      />

      {notice && <Alert variant="success">{notice}</Alert>}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="relative w-full sm:max-w-sm">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-muted"
            aria-hidden="true"
          />
          <Input
            className="pl-9"
            placeholder="Buscar por nombre o email"
            aria-label="Buscar usuarios"
            value={filters.searchInput}
            onChange={(event) => filters.setSearchInput(event.target.value)}
          />
        </div>
        <Select value={filters.role} onValueChange={(value) => filters.update('role', value)}>
          <SelectTrigger className="w-full sm:w-48" aria-label="Filtrar por rol">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="ALL">Todos los roles</SelectItem>
            <SelectItem value="TOURIST">Viajeros</SelectItem>
            <SelectItem value="PROVIDER">Operadores</SelectItem>
            <SelectItem value="ADMIN">Administradores</SelectItem>
          </SelectContent>
        </Select>
        <Select value={filters.status} onValueChange={(value) => filters.update('status', value)}>
          <SelectTrigger className="w-full sm:w-48" aria-label="Filtrar por estado">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="ALL">Todos los estados</SelectItem>
            <SelectItem value="ACTIVE">Activas</SelectItem>
            <SelectItem value="SUSPENDED">Suspendidas</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {users.isError && <Alert variant="destructive">{getErrorMessage(users.error)}</Alert>}

      <Card className="overflow-hidden p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Usuario</TableHead>
              <TableHead>Rol</TableHead>
              <TableHead>Empresa</TableHead>
              <TableHead>Alta</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>
                <span className="sr-only">Acciones</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {users.isPending ? (
              <TableLoadingRow colSpan={6} />
            ) : items.length === 0 ? (
              <TableEmptyRow
                colSpan={6}
                title="No hay cuentas que coincidan"
                description="Probá con otro rol, otro estado o con otra búsqueda."
              />
            ) : (
              items.map((row) => {
                const isSelf = row.id === user?.id
                const suspended = row.status === 'SUSPENDED'
                return (
                  <TableRow key={row.id}>
                    <TableCell>
                      <span className="block font-medium text-foreground">
                        {fullName(row)}
                        {isSelf && <span className="ml-2 text-label font-normal text-ink-muted">(vos)</span>}
                      </span>
                      <span className="text-caption text-ink-muted">{row.email}</span>
                    </TableCell>
                    <TableCell>{ROLE_LABEL[row.role ?? ''] ?? row.role}</TableCell>
                    <TableCell>{row.companyName ?? <span className="text-ink-muted">—</span>}</TableCell>
                    <TableCell className="tabular-nums">
                      {row.createdAt ? new Date(row.createdAt).toLocaleDateString('es-BO') : ''}
                    </TableCell>
                    <TableCell>
                      <Badge variant={suspended ? 'destructive' : 'success'}>
                        {STATUS_LABEL[row.status ?? ''] ?? row.status}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right">
                      {isSelf && !suspended ? null : (
                        <Button
                          variant={suspended ? 'outline' : 'ghost'}
                          size="sm"
                          disabled={change.isPending}
                          onClick={() => ask(row)}
                          aria-label={`${suspended ? 'Activar' : 'Suspender'} la cuenta de ${fullName(row)}`}
                        >
                          {suspended ? 'Activar' : 'Suspender'}
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                )
              })
            )}
          </TableBody>
        </Table>

        {totalPages > 1 && (
          <div className="flex items-center justify-between gap-3 border-t border-border px-4 py-3">
            <p className="text-label text-ink-muted">
              Página {filters.page} de {totalPages}
            </p>
            <div className="flex gap-2">
              <Button variant="outline" disabled={filters.page <= 1} onClick={() => filters.goToPage(filters.page - 1)}>
                Anterior
              </Button>
              <Button
                variant="outline"
                disabled={filters.page >= totalPages}
                onClick={() => filters.goToPage(filters.page + 1)}
              >
                Siguiente
              </Button>
            </div>
          </div>
        )}
      </Card>

      <ConfirmActionDialog
        open={target !== null}
        title={target?.action === 'activate' ? '¿Activar la cuenta?' : '¿Suspender la cuenta?'}
        confirmLabel={target?.action === 'activate' ? 'Activar' : 'Suspender'}
        destructive={target?.action === 'suspend'}
        pending={change.isPending}
        error={error}
        onConfirm={confirm}
        onClose={() => setTarget(null)}
      >
        {target?.action === 'activate' ? (
          <p>
            {target.name} ({target.email}) va a poder volver a ingresar con su contraseña de siempre.
          </p>
        ) : (
          <>
            <p>
              {target?.name} ({target?.email}) no va a poder ingresar ni renovar su sesión hasta que alguien la
              vuelva a activar. No se borra nada de la cuenta.
            </p>
            {target?.role === 'PROVIDER' && (
              <p>Sus experiencias y paquetes siguen publicados: para retirarlos, suspendé el contenido o la empresa.</p>
            )}
          </>
        )}
      </ConfirmActionDialog>
    </div>
  )
}
