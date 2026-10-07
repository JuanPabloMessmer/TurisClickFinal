import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { Copy, KeyRound } from 'lucide-react'
import { useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import { getErrorMessage } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'

/**
 * Los accesos del equipo de una empresa.
 *
 * Lo único que se puede hacer desde acá es regenerar la credencial de alguien que perdió la suya. No se muestra
 * ninguna contraseña existente —no se guardan en claro, así que no hay nada que mostrar— y regenerar corta las
 * sesiones abiertas de esa cuenta: una credencial nueva que conviva con la vieja no protege de nada.
 */
/** Los estados de cuenta tienen nombre propio en la pantalla: nadie tiene por qué leer el enum del backend. */
const ACCOUNT_STATUS_LABEL: Record<string, string> = {
  SUSPENDED: 'Suspendida',
  PENDING: 'Sin activar',
}

export function CompanyAccountsCard({ companyId }: { companyId: string }) {
  const queryClient = useQueryClient()
  const [regenerated, setRegenerated] = useState<{ email: string; password: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const users = useQuery({
    queryKey: ['admin', 'company-users', companyId],
    queryFn: () => adminApi.listCompanyUsers(httpClient, companyId),
  })

  const reset = useMutation({
    mutationFn: (userId: string) => adminApi.resetProviderPassword(httpClient, userId),
    onSuccess: (result) => {
      setRegenerated({ email: result.email ?? '', password: result.temporaryPassword ?? '' })
      setCopied(false)
      setError(null)
      void queryClient.invalidateQueries({ queryKey: ['admin', 'company-users', companyId] })
    },
    onError: (mutationError) => setError(getErrorMessage(mutationError)),
  })

  const accounts = users.data ?? []

  return (
    <Card className="mb-6">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-heading">
          <KeyRound className="h-4 w-4 text-ink-muted" aria-hidden="true" />
          Accesos
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {users.isPending ? (
          <div className="flex justify-center py-6">
            <Spinner />
          </div>
        ) : accounts.length === 0 ? (
          <p className="text-body text-ink-muted">Esta empresa no tiene cuentas asociadas.</p>
        ) : (
          <ul className="divide-y divide-border">
            {accounts.map((account) => (
              <li key={account.id} className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0">
                <div className="min-w-0">
                  <p className="truncate text-body font-medium text-foreground">{account.fullName}</p>
                  <p className="truncate text-label text-ink-muted">{account.email}</p>
                </div>
                <div className="flex items-center gap-2">
                  {account.mustChangePassword && (
                    <Badge variant="warning">Todavía no entró</Badge>
                  )}
                  {account.status !== 'ACTIVE' && (
                    <Badge variant="destructive">{ACCOUNT_STATUS_LABEL[account.status ?? ''] ?? 'Sin acceso'}</Badge>
                  )}
                  <Button
                    variant="outline"
                    size="sm"
                    loading={reset.isPending && reset.variables === account.id}
                    onClick={() => reset.mutate(account.id!)}
                  >
                    Regenerar contraseña
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}

        {error && <Alert variant="destructive">{error}</Alert>}

        {regenerated && (
          <Alert variant="warning">
            <p className="font-medium">Contraseña nueva para {regenerated.email}</p>
            <p className="mt-1">
              Se muestra una sola vez y las sesiones abiertas de esa cuenta se cerraron. En su próximo ingreso va
              a tener que elegir una contraseña propia.
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
            </div>
          </Alert>
        )}
      </CardContent>
    </Card>
  )
}
