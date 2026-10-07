import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
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
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { getErrorMessage } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'

/**
 * Alta de un operador: la empresa y su primera cuenta, en un solo paso.
 *
 * Reemplaza el autorregistro público. En TurisClick una empresa de turismo no se da de alta sola: alguien de la
 * plataforma la conoce, la carga acá y le entrega sus credenciales.
 *
 * La contraseña temporal la genera el servidor y **se muestra una sola vez**: no se guarda en claro y no se
 * puede volver a consultar. La pantalla lo dice con esas palabras, porque descubrirlo después es el camino
 * seguro a una cuenta inutilizable.
 */
const schema = z.object({
  companyName: z.string().min(2, 'Ingresá el nombre de la empresa.').max(150),
  companyDescription: z.string().max(2000).optional().or(z.literal('')),
  legalDocument: z.string().min(3, 'Ingresá el NIT o documento legal.').max(50),
  contactEmail: z.string().email('Ingresá un correo válido.'),
  contactPhone: z.string().max(30).optional().or(z.literal('')),
  firstName: z.string().min(2, 'Ingresá el nombre.').max(100),
  lastName: z.string().min(2, 'Ingresá el apellido.').max(100),
  email: z.string().email('Ingresá el correo con el que va a entrar.'),
})

type FormValues = z.infer<typeof schema>

export function NewProviderAccountPage() {
  const [created, setCreated] = useState<adminApi.ProviderAccountCreatedResponse | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      companyName: '',
      companyDescription: '',
      legalDocument: '',
      contactEmail: '',
      contactPhone: '',
      firstName: '',
      lastName: '',
      email: '',
    },
  })

  const create = useMutation({
    mutationFn: (values: FormValues) =>
      adminApi.createProviderAccount(httpClient, {
        ...values,
        companyDescription: values.companyDescription || undefined,
        contactPhone: values.contactPhone || undefined,
        approve: true,
      }),
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
          title="Operador dado de alta"
          description={`${created.company?.name} ya puede entrar a TurisClick.`}
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
              entregásela al operador. Si se pierde, se genera una nueva desde la ficha de la empresa.
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
              puede publicar nada.
            </p>

            <div className="flex flex-wrap gap-2">
              <Link to={`/admin/companies/${created.company?.id}`}>
                <Button variant="outline">Ver la empresa</Button>
              </Link>
              <Button
                variant="ghost"
                onClick={() => {
                  setCreated(null)
                  setCopied(false)
                  form.reset()
                }}
              >
                Dar de alta otro operador
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
        <Link to="/admin/companies" className="flex items-center gap-1.5 text-label font-medium text-primary hover:underline">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Empresas
        </Link>
        <PageHeader
          title="Dar de alta un operador"
          description="Cargá la empresa y la persona que va a administrarla. Al guardar se genera una contraseña temporal para entregarle."
        />
      </div>

      <form className="flex flex-col gap-6" onSubmit={form.handleSubmit((values) => create.mutate(values))} noValidate>
        <Card>
          <CardHeader>
            <CardTitle className="text-heading">La empresa</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor="companyName">Nombre</Label>
              <Input id="companyName" {...form.register('companyName')} />
              {form.formState.errors.companyName && (
                <p className="text-xs text-destructive">{form.formState.errors.companyName.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="legalDocument">NIT o documento legal</Label>
              <Input id="legalDocument" {...form.register('legalDocument')} />
              {form.formState.errors.legalDocument && (
                <p className="text-xs text-destructive">{form.formState.errors.legalDocument.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="contactPhone">Teléfono de contacto</Label>
              <Input id="contactPhone" {...form.register('contactPhone')} />
            </div>

            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor="contactEmail">Correo de contacto de la empresa</Label>
              <Input id="contactEmail" type="email" {...form.register('contactEmail')} />
              {form.formState.errors.contactEmail && (
                <p className="text-xs text-destructive">{form.formState.errors.contactEmail.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5 sm:col-span-2">
              <Label htmlFor="companyDescription">Qué hace (opcional)</Label>
              <Textarea id="companyDescription" rows={3} {...form.register('companyDescription')} />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-heading">Quién la administra</CardTitle>
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
              <p className="text-caption text-ink-muted">Puede ser distinto del correo de contacto de la empresa.</p>
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
          <Link to="/admin/companies">
            <Button type="button" variant="ghost">
              Cancelar
            </Button>
          </Link>
        </div>
      </form>
    </div>
  )
}
