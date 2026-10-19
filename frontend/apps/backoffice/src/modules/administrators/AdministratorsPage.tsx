import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { Copy, Plus } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '@/auth/useAuth'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import { getErrorMessage } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'

/**
 * Las cuentas de administrador y sus accesos.
 *
 * Lo único que se hace sobre una cuenta existente es regenerar la credencial de alguien que perdió la suya, con
 * el mismo mecanismo que para los operadores: contraseña nueva generada por el servidor, mostrada una sola vez,
 * sesiones abiertas cortadas y cambio obligatorio en el próximo ingreso. Como es el rol con más permisos, se
 * pide confirmación antes. La propia no se regenera desde acá: se cambia con la contraseña actual.
 */
const ACCOUNT_STATUS_LABEL: Record<string, string> = {
  SUSPENDED: 'Suspendida',
  PENDING: 'Sin activar',
}

export function AdministratorsPage() {
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState<string | null>(null)
  const [regenerated, setRegenerated] = useState<{ email: string; password: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const administrators = useQuery({
    queryKey: ['admin', 'administrators'],
    queryFn: () => adminApi.listAdministrators(httpClient),
  })

  const reset = useMutation({
    mutationFn: (userId: string) => adminApi.resetAdminPassword(httpClient, userId),
    onSuccess: (result) => {
      setRegenerated({ email: result.email ?? '', password: result.temporaryPassword ?? '' })
      setConfirming(null)
      setCopied(false)
      setError(null)
      void queryClient.invalidateQueries({ queryKey: ['admin', 'administrators'] })
    },
    onError: (mutationError) => {
      setConfirming(null)
      setError(getErrorMessage(mutationError))
    },
  })

  const accounts = administrators.data?.items ?? []

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <PageHeader
          title="Administradores"
          description="Las personas que gestionan toda la plataforma. Desde acá das de alta a una nueva y regenerás la contraseña de quien perdió la suya."
        />
        <Button asChild>
          <Link to="/admin/administrators/new">
            <Plus className="h-4 w-4" aria-hidden="true" />
            Dar de alta un administrador
          </Link>
        </Button>
      </div>

      {regenerated && (
        <Alert variant="warning">
          <p className="font-medium">Contraseña nueva para {regenerated.email}</p>
          <p className="mt-1">
            Se muestra una sola vez y las sesiones abiertas de esa cuenta se cerraron. Entregásela por un canal
            distinto del que usás para mandarle el enlace de ingreso. En su próximo ingreso va a tener que elegir
            una contraseña propia.
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <code className="rounded-sm bg-surface px-2 py-1 font-mono text-body">{regenerated.password}</code>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => {
                void navigator.clipboard?.writeText(regenerated.password)
                setCopied(true)
              }}
            >
              <Copy className="h-3.5 w-3.5" aria-hidden="true" />
              {copied ? 'Copiada' : 'Copiar'}
            </Button>
            <Button type="button" variant="ghost" size="sm" onClick={() => setRegenerated(null)}>
              Listo, ya la entregué
            </Button>
          </div>
        </Alert>
      )}

      {error && <Alert variant="destructive">{error}</Alert>}

      <Card>
        <CardContent className="pt-6">
          {administrators.isPending ? (
            <div className="flex justify-center py-6">
              <Spinner />
            </div>
          ) : administrators.isError ? (
            <Alert variant="destructive">{getErrorMessage(administrators.error)}</Alert>
          ) : (
            <ul className="divide-y divide-border">
              {accounts.map((account) => {
                const isSelf = account.id === user?.id
                const fullName = `${account.firstName ?? ''} ${account.lastName ?? ''}`.trim()
                return (
                  <li key={account.id} className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0">
                    <div className="min-w-0">
                      <p className="truncate text-body font-medium text-foreground">
                        {fullName}
                        {isSelf && <span className="ml-2 text-label font-normal text-ink-muted">(vos)</span>}
                      </p>
                      <p className="truncate text-label text-ink-muted">{account.email}</p>
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                      {account.mustChangePassword && <Badge variant="warning">Contraseña temporal pendiente</Badge>}
                      {account.status !== 'ACTIVE' && (
                        <Badge variant="destructive">
                          {ACCOUNT_STATUS_LABEL[account.status ?? ''] ?? 'Sin acceso'}
                        </Badge>
                      )}
                      {!isSelf &&
                        (confirming === account.id ? (
                          <>
                            <span className="text-label text-ink-muted">Se cierran sus sesiones.</span>
                            <Button
                              size="sm"
                              loading={reset.isPending && reset.variables === account.id}
                              onClick={() => reset.mutate(account.id!)}
                            >
                              Confirmar
                            </Button>
                            <Button size="sm" variant="ghost" onClick={() => setConfirming(null)}>
                              Cancelar
                            </Button>
                          </>
                        ) : (
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => {
                              setConfirming(account.id!)
                              setError(null)
                            }}
                          >
                            Regenerar contraseña
                          </Button>
                        ))}
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
