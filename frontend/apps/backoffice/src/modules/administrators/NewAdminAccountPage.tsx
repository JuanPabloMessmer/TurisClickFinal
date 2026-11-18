import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { LIMITS, applyServerErrors, emailSchema, personNameSchema, rules, singleLine } from '@turisclick/validation'
import { ArrowLeft, Copy, KeyRound } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { FormField } from '@/components/form/FormField'
import { fieldAria } from '@/components/form/fieldAria'
import { httpClient } from '@/lib/httpClient'

/**
 * Alta de otro administrador, con el mismo mecanismo que el alta de operadores: la contraseña temporal la genera
 * el servidor, **se muestra una sola vez** y la cuenta no puede operar hasta cambiarla.
 *
 * Es el rol con más permisos de TurisClick, así que la pantalla no guarda la credencial en ningún lado —sólo
 * vive en el estado del componente mientras se la entrega— y recomienda pasarla por un canal distinto del que
 * lleva el enlace de ingreso.
 */
// Reglas R-04 y R-06 (docs/auditoria-validaciones.md): las mismas que valida POST /api/admin/admin-accounts.
const schema = z.object({
  firstName: personNameSchema('El nombre'),
  lastName: personNameSchema('El apellido'),
  email: emailSchema('El correo de acceso'),
})

type FormValues = z.infer<typeof schema>

const FIELDS = ['firstName', 'lastName', 'email'] as const satisfies readonly (keyof FormValues)[]

export function NewAdminAccountPage() {
  const [created, setCreated] = useState<adminApi.AdminAccountCreatedResponse | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    mode: 'onTouched',
    defaultValues: { firstName: '', lastName: '', email: '' },
  })

  const create = useMutation({
    // Se envía ya normalizado (el servidor lo vuelve a normalizar igual).
    mutationFn: (values: FormValues) =>
      adminApi.createAdminAccount(httpClient, {
        firstName: singleLine(values.firstName),
        lastName: singleLine(values.lastName),
        email: rules.normalizeEmail(values.email) ?? '',
      }),
    onSuccess: (result) => {
      setCreated(result)
      setSubmitError(null)
    },
    onError: (error) => setSubmitError(applyServerErrors(error, form.setError, FIELDS)),
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
              <Link to="/admin/administrators">
                <Button variant="outline">Ver administradores</Button>
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
      <div className="flex flex-col gap-2">
        <Link
          to="/admin/administrators"
          className="flex items-center gap-1.5 text-label font-medium text-primary hover:underline"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Administradores
        </Link>
        <PageHeader
          title="Dar de alta un administrador"
          description="Un administrador ve y gestiona toda la plataforma: empresas, catálogo, reservas y pagos. Al guardar se genera una contraseña temporal para entregarle."
        />
      </div>

      <form className="flex flex-col gap-6" onSubmit={form.handleSubmit((values) => create.mutate(values))} noValidate>
        <Card>
          <CardHeader>
            <CardTitle className="text-heading">La persona</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <FormField id="firstName" label="Nombre" error={form.formState.errors.firstName?.message}>
              <Input {...fieldAria('firstName', form.formState.errors.firstName?.message)} {...form.register('firstName')} />
            </FormField>

            <FormField id="lastName" label="Apellido" error={form.formState.errors.lastName?.message}>
              <Input {...fieldAria('lastName', form.formState.errors.lastName?.message)} {...form.register('lastName')} />
            </FormField>

            <FormField
              id="email"
              label="Correo con el que va a entrar"
              error={form.formState.errors.email?.message}
              className="sm:col-span-2"
            >
              <Input
                {...fieldAria('email', form.formState.errors.email?.message)}
                type="email"
                autoComplete="off"
                maxLength={LIMITS.emailMax}
                {...form.register('email')}
              />
            </FormField>
          </CardContent>
        </Card>

        {submitError && <Alert variant="destructive">{submitError}</Alert>}

        <div className="flex flex-wrap gap-2">
          <Button type="submit" loading={create.isPending}>
            Dar de alta y generar credenciales
          </Button>
          <Link to="/admin/administrators">
            <Button type="button" variant="ghost">
              Cancelar
            </Button>
          </Link>
        </div>
      </form>
    </div>
  )
}
