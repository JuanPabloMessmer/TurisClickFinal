import { zodResolver } from '@hookform/resolvers/zod'
import { companiesApi } from '@turisclick/api-client'
import { type ReactNode, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { httpClient } from '@/lib/httpClient'
import { getErrorMessage } from '@/lib/errors'
import { cn } from '@/lib/utils'
import { authManager } from './authManager'
import { useAuth } from './useAuth'

/**
 * UC-P-01. No estaba en la lista original de pantallas del Backoffice, pero sin ella no hay forma de
 * generar un PROVIDER desde la UI para probar "Companies approval"/"Mi Empresa" sin recurrir a
 * Postman/Swagger — se agrega como parte mínima necesaria del flujo de auth. Señalado explícitamente en
 * el reporte final para que se pueda revisar/vetar.
 */
const schema = z.object({
  firstName: z.string().min(2).max(100),
  lastName: z.string().min(2).max(100),
  email: z.string().email(),
  password: z.string().min(8, 'Mínimo 8 caracteres.').max(100),
  companyName: z.string().min(2).max(150),
  companyDescription: z.string().max(1000).optional().or(z.literal('')),
  legalDocument: z.string().min(1).max(50),
  contactEmail: z.string().email(),
  contactPhone: z.string().max(30).optional().or(z.literal('')),
})

type FormValues = z.infer<typeof schema>

export function RegisterProviderPage() {
  const { status, user } = useAuth()
  const navigate = useNavigate()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      firstName: '',
      lastName: '',
      email: '',
      password: '',
      companyName: '',
      companyDescription: '',
      legalDocument: '',
      contactEmail: '',
      contactPhone: '',
    },
  })

  if (status === 'authenticated' && user) {
    return <Navigate to={user.role === 'ADMIN' ? '/admin/destinations' : '/provider/company'} replace />
  }

  const onSubmit = async (values: FormValues) => {
    setSubmitError(null)
    try {
      const result = await companiesApi.registerProvider(httpClient, {
        ...values,
        companyDescription: values.companyDescription || undefined,
        contactPhone: values.contactPhone || undefined,
      })
      authManager.applyExternalSession(result)
      navigate('/provider/company', { replace: true })
    } catch (error) {
      setSubmitError(getErrorMessage(error))
    }
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-primary px-4 py-12">
      <div className="mb-8 flex flex-col items-center gap-3">
        <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-secondary text-xl font-bold text-secondary-foreground">
          T
        </div>
        <div className="text-center">
          <p className="text-xl font-bold tracking-tight text-primary-foreground">TurisClick</p>
          <p className="text-xs font-medium uppercase tracking-wider text-primary-foreground/60">Backoffice</p>
        </div>
      </div>

      <Card className="w-full max-w-lg">
        <CardHeader>
          <CardTitle>Registrar empresa</CardTitle>
          <CardDescription>UC-P-01. Tu empresa queda pendiente de aprobación por un administrador.</CardDescription>
        </CardHeader>
        <CardContent>
          <form className="grid grid-cols-1 gap-4 sm:grid-cols-2" onSubmit={form.handleSubmit(onSubmit)} noValidate>
            <Field label="Nombre" error={form.formState.errors.firstName?.message}>
              <Input {...form.register('firstName')} />
            </Field>
            <Field label="Apellido" error={form.formState.errors.lastName?.message}>
              <Input {...form.register('lastName')} />
            </Field>
            <Field label="Email" error={form.formState.errors.email?.message} className="sm:col-span-2">
              <Input type="email" autoComplete="username" {...form.register('email')} />
            </Field>
            <Field label="Contraseña" error={form.formState.errors.password?.message} className="sm:col-span-2">
              <Input type="password" autoComplete="new-password" {...form.register('password')} />
            </Field>
            <Field label="Nombre de la empresa" error={form.formState.errors.companyName?.message} className="sm:col-span-2">
              <Input {...form.register('companyName')} />
            </Field>
            <Field
              label="Descripción de la empresa (opcional)"
              error={form.formState.errors.companyDescription?.message}
              className="sm:col-span-2"
            >
              <Input {...form.register('companyDescription')} />
            </Field>
            <Field label="Documento legal (NIT/RUC)" error={form.formState.errors.legalDocument?.message}>
              <Input {...form.register('legalDocument')} />
            </Field>
            <Field label="Email de contacto" error={form.formState.errors.contactEmail?.message}>
              <Input type="email" {...form.register('contactEmail')} />
            </Field>
            <Field label="Teléfono de contacto (opcional)" error={form.formState.errors.contactPhone?.message} className="sm:col-span-2">
              <Input {...form.register('contactPhone')} />
            </Field>
            {submitError && (
              <Alert variant="destructive" className="sm:col-span-2">
                {submitError}
              </Alert>
            )}
            <Button type="submit" disabled={form.formState.isSubmitting} className="sm:col-span-2">
              {form.formState.isSubmitting ? 'Registrando…' : 'Registrar empresa'}
            </Button>
          </form>
          <p className="mt-5 text-center text-sm text-muted-foreground">
            ¿Ya tenés cuenta?{' '}
            <Link to="/login" className="font-medium text-primary underline-offset-4 hover:underline">
              Iniciá sesión
            </Link>
          </p>
        </CardContent>
      </Card>
    </div>
  )
}

function Field({
  label,
  error,
  className,
  children,
}: {
  label: string
  error?: string
  className?: string
  children: ReactNode
}) {
  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <Label>{label}</Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  )
}
