import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { Copy, KeyRound } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { getErrorMessage } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'

/**
 * Alta de otro administrador, con el mismo mecanismo que el alta de operadores: la contraseña temporal la genera
 * el servidor, **se muestra una sola vez** y la cuenta no puede operar hasta cambiarla.
 *
 * Es el rol con más permisos de TurisClick, así que la pantalla no guarda la credencial en ningún lado —sólo
 * vive en el estado del componente mientras se la entrega— y recomienda pasarla por un canal distinto del que
 * lleva el enlace de ingreso.
 */
const schema = z.object({
  firstName: z.string().trim().min(2, 'Ingresá el nombre.').max(100),
  lastName: z.string().trim().min(2, 'Ingresá el apellido.').max(100),
  email: z.string().trim().email('Ingresá el correo con el que va a entrar.').max(150),
})

type FormValues = z.infer<typeof schema>

export function NewAdminAccountPage() {
  const [created, setCreated] = useState<adminApi.AdminAccountCreatedResponse | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: '', lastName: '', email: '' },
  })

  const create = useMutation({
    mutationFn: (values: FormValues) => adminApi.createAdminAccount(httpClient, values),
    onSuccess: (result) => {
      setCreated(result)
      setSubmitError(null)
    },
    onError: (error) => setSubmitError(getErrorMessage(error)),
  })

  if (created) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader
          title="Administrador dado de alta"
          description={`${created.fullName} ya puede entrar al panel de TurisClick.`}
        />

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-heading">
              <KeyRound className="h-4 w-4 text-ink-muted" aria-hidden="true" />
              Credenciales para entregar
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <Alert variant="warning">
              Esta contraseña se muestra una sola vez: no queda guardada en ningún lado. Copiala ahora y
              entregásela por un canal distinto del que usás para mandarle el enlace de ingreso.
            </Alert>

            <div className="flex flex-col gap-1.5">
              <p className="text-label text-ink-muted">Usuario</p>
              <p className="text-body font-medium text-foreground">{created.email}</p>
            </div>

            <div className="flex flex-col gap-1.5">
              <p className="text-label text-ink-muted">Contraseña temporal</p>
              <div className="flex flex-wrap items-center gap-2">
                <code className="rounded-sm bg-muted px-3 py-2 font-mono text-body text-foreground">
                  {created.temporaryPassword}
                </code>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    void navigator.clipboard?.writeText(created.temporaryPassword ?? '')
                    setCopied(true)
                  }}
                >
                  <Copy className="h-4 w-4" aria-hidden="true" />
                  {copied ? 'Copiada' : 'Copiar'}
                </Button>
              </div>
            </div>

            <p className="text-label text-ink-muted">
              En su primer ingreso va a tener que elegir una contraseña propia. Hasta que lo haga, su cuenta no
              puede ver ni modificar nada de la plataforma.
            </p>

            <div className="flex flex-wrap gap-2">
              <Link to="/dashboard">
                <Button variant="outline">Volver a Hoy</Button>
              </Link>
              <Button
                variant="ghost"
                onClick={() => {
                  setCreated(null)
                  setCopied(false)
                  form.reset()
                }}
              >
                Dar de alta otro administrador
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Dar de alta un administrador"
        description="Un administrador ve y gestiona toda la plataforma: empresas, catálogo, reservas y pagos. Al guardar se genera una contraseña temporal para entregarle."
      />

      <form className="flex flex-col gap-6" onSubmit={form.handleSubmit((values) => create.mutate(values))} noValidate>
        <Card>
          <CardHeader>
            <CardTitle className="text-heading">La persona</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="firstName">Nombre</Label>
              <Input id="firstName" {...form.register('firstName')} />
              {form.formState.errors.firstName && (
                <p className="text-xs text-destructive">{form.formState.errors.firstName.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="lastName">Apellido</Label>
              <Input id="lastName" {...form.register('lastName')} />
              {form.formState.errors.lastName && (
                <p className="text-xs text-destructive">{form.formState.errors.lastName.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor="email">Correo con el que va a entrar</Label>
              <Input id="email" type="email" autoComplete="off" {...form.register('email')} />
              {form.formState.errors.email && (
                <p className="text-xs text-destructive">{form.formState.errors.email.message}</p>
              )}
            </div>
          </CardContent>
        </Card>

        {submitError && <Alert variant="destructive">{submitError}</Alert>}

        <div className="flex flex-wrap gap-2">
          <Button type="submit" loading={create.isPending}>
            Dar de alta y generar credenciales
          </Button>
          <Link to="/dashboard">
            <Button type="button" variant="ghost">
              Cancelar
            </Button>
          </Link>
        </div>
      </form>
    </div>
  )
}
