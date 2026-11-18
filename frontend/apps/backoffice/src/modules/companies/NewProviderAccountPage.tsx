import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { adminApi, companiesApi } from '@turisclick/api-client'
import {
  LIMITS,
  applyServerErrors,
  contactPhoneSchema,
  emailSchema,
  nitSchema,
  organizationNameSchema,
  personNameSchema,
  plainTextSchema,
  rules,
  singleLine,
} from '@turisclick/validation'
import { ArrowLeft, Copy, KeyRound } from 'lucide-react'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { FormField } from '@/components/form/FormField'
import { fieldAria } from '@/components/form/fieldAria'
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
// Reglas R-01 a R-06 (docs/auditoria-validaciones.md): las mismas, con los mismos mensajes, que valida el backend.
const schema = z.object({
  companyName: organizationNameSchema(),
  companyDescription: plainTextSchema('La descripción', LIMITS.companyDescriptionMax, { multiLine: true }),
  legalDocument: nitSchema(),
  contactEmail: emailSchema('El correo de contacto'),
  contactPhone: contactPhoneSchema(),
  firstName: personNameSchema('El nombre'),
  lastName: personNameSchema('El apellido'),
  email: emailSchema('El correo de acceso'),
})

type FormValues = z.infer<typeof schema>

const FIELDS = [
  'companyName',
  'companyDescription',
  'legalDocument',
  'contactEmail',
  'contactPhone',
  'firstName',
  'lastName',
  'email',
] as const satisfies readonly (keyof FormValues)[]

export function NewProviderAccountPage() {
  const [created, setCreated] = useState<adminApi.ProviderAccountCreatedResponse | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const [similar, setSimilar] = useState<companiesApi.CompanyNameMatch[]>([])
  // Duplicado detectado al salir del nombre. Vive aparte de los errores del formulario: la validación de
  // react-hook-form al salir del campo corre en paralelo y, si terminara después, lo borraría.
  const [nameConflict, setNameConflict] = useState<string | null>(null)

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    mode: 'onTouched',
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
    onError: (error) => setSubmitError(applyServerErrors(error, form.setError, FIELDS)),
  })

  /**
   * Al salir del nombre se consulta si ya hay una empresa igual (el alta la va a rechazar) o parecida (sólo
   * cambia la puntuación o la forma societaria: se avisa y se deja seguir). Un fallo de esta consulta no bloquea
   * nada: el servidor vuelve a verificar al guardar.
   */
  const checkName = async (raw: string) => {
    const name = singleLine(raw)
    setNameConflict(null)
    if (rules.organizationName(name)) {
      setSimilar([])
      return
    }
    try {
      const result = await companiesApi.checkCompanyName(httpClient, name)
      setSimilar(result.similar ?? [])
      if (result.exactMatch?.name) setNameConflict(`Ya existe una empresa con ese nombre: «${result.exactMatch.name}».`)
    } catch {
      setSimilar([])
    }
  }

  const errors = form.formState.errors
  const companyNameError = errors.companyName?.message ?? nameConflict ?? undefined
  const description = useWatch({ control: form.control, name: 'companyDescription' }) ?? ''
  const phone = rules.parsePhone(useWatch({ control: form.control, name: 'contactPhone' }))

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
                  setSimilar([])
                  setNameConflict(null)
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
            <FormField id="companyName" label="Nombre" error={companyNameError} className="sm:col-span-2">
              <Input
                {...fieldAria('companyName', companyNameError)}
                maxLength={LIMITS.organizationNameMax + 20}
                {...form.register('companyName', { onBlur: (e) => void checkName(e.target.value) })}
              />
              {similar.length > 0 && !companyNameError && (
                <Alert variant="warning">
                  Hay {similar.length === 1 ? 'una empresa con un nombre parecido' : 'empresas con nombres parecidos'}:{' '}
                  {similar.map((s) => `«${s.name}»`).join(', ')}. Verificá que no sea la misma antes de darla de alta.
                </Alert>
              )}
            </FormField>

            <FormField
              id="legalDocument"
              label="NIT"
              error={errors.legalDocument?.message}
              hint="Sólo números, de 5 a 12 dígitos. Podés pegarlo con puntos o guiones."
            >
              <Input
                {...fieldAria('legalDocument', errors.legalDocument?.message, true)}
                inputMode="numeric"
                autoComplete="off"
                {...form.register('legalDocument')}
              />
            </FormField>

            <FormField
              id="contactPhone"
              label="Teléfono de contacto (opcional)"
              error={errors.contactPhone?.message}
              hint={phone.kind ? `Se guarda como ${phone.value} (${phone.kind}).` : 'Fijo, celular o 800 de Bolivia, o un número del exterior con +.'}
            >
              <Input
                {...fieldAria('contactPhone', errors.contactPhone?.message, true)}
                type="tel"
                inputMode="tel"
                {...form.register('contactPhone')}
              />
            </FormField>

            <FormField
              id="contactEmail"
              label="Correo de contacto de la empresa"
              error={errors.contactEmail?.message}
              className="sm:col-span-2"
            >
              <Input
                {...fieldAria('contactEmail', errors.contactEmail?.message)}
                type="email"
                maxLength={LIMITS.emailMax}
                {...form.register('contactEmail')}
              />
            </FormField>

            <FormField
              id="companyDescription"
              label="Qué hace (opcional)"
              error={errors.companyDescription?.message}
              counter={{ length: description.length, max: LIMITS.companyDescriptionMax }}
              className="sm:col-span-2"
            >
              <Textarea
                {...fieldAria('companyDescription', errors.companyDescription?.message)}
                rows={3}
                {...form.register('companyDescription')}
              />
            </FormField>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-heading">Quién la administra</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <FormField id="firstName" label="Nombre" error={errors.firstName?.message}>
              <Input {...fieldAria('firstName', errors.firstName?.message)} autoComplete="off" {...form.register('firstName')} />
            </FormField>

            <FormField id="lastName" label="Apellido" error={errors.lastName?.message}>
              <Input {...fieldAria('lastName', errors.lastName?.message)} autoComplete="off" {...form.register('lastName')} />
            </FormField>

            <FormField
              id="email"
              label="Correo con el que va a entrar"
              error={errors.email?.message}
              hint="Puede ser distinto del correo de contacto de la empresa."
              className="sm:col-span-2"
            >
              <Input
                {...fieldAria('email', errors.email?.message, true)}
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
