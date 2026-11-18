import { zodResolver } from '@hookform/resolvers/zod'
import {
  LIMITS,
  applyServerErrors,
  contactPhoneSchema,
  emailSchema,
  organizationNameSchema,
  plainTextSchema,
  rules,
} from '@turisclick/validation'
import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { FormField } from '@/components/form/FormField'
import { fieldAria } from '@/components/form/fieldAria'
import { useMyCompany, useUpdateMyCompany } from './api'

// Reglas R-01, R-02, R-04 y R-05 (docs/auditoria-validaciones.md): las mismas que valida PUT /api/companies/me.
const schema = z.object({
  name: organizationNameSchema(),
  description: plainTextSchema('La descripción', LIMITS.companyDescriptionMax, { multiLine: true }),
  contactEmail: emailSchema('El correo de contacto'),
  contactPhone: contactPhoneSchema(),
})

type FormValues = z.infer<typeof schema>

const FIELDS = ['name', 'description', 'contactEmail', 'contactPhone'] as const satisfies readonly (keyof FormValues)[]

const statusVariant: Record<string, 'success' | 'warning' | 'destructive' | 'neutral'> = {
  APPROVED: 'success',
  PENDING_APPROVAL: 'warning',
  REJECTED: 'destructive',
  SUSPENDED: 'destructive',
}

const statusLabel: Record<string, string> = {
  APPROVED: 'Aprobada',
  PENDING_APPROVAL: 'Pendiente de aprobación',
  REJECTED: 'Rechazada',
  SUSPENDED: 'Suspendida',
}

export function MyCompanyPage() {
  const { data: company, isLoading } = useMyCompany()
  const updateMutation = useUpdateMyCompany()
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    mode: 'onTouched',
    values: company
      ? {
          name: company.name ?? '',
          description: company.description ?? '',
          contactEmail: company.contactEmail ?? '',
          contactPhone: company.contactPhone ?? '',
        }
      : undefined,
  })

  const errors = form.formState.errors
  const description = useWatch({ control: form.control, name: 'description' }) ?? ''
  const phone = rules.parsePhone(useWatch({ control: form.control, name: 'contactPhone' }))

  if (isLoading || !company) return <FullScreenSpinner />

  const onSubmit = async (values: FormValues) => {
    setSubmitError(null)
    setSuccess(false)
    try {
      await updateMutation.mutateAsync({
        ...values,
        description: values.description || undefined,
        contactPhone: values.contactPhone || undefined,
      })
      setSuccess(true)
    } catch (error) {
      setSubmitError(applyServerErrors(error, form.setError, FIELDS))
    }
  }

  return (
    <div className="max-w-2xl">
      <PageHeader title="Mi Empresa" description="Los datos con los que tu empresa aparece en TurisClick." />

      <Card className="mb-6">
        <CardHeader className="flex-row items-center justify-between">
          <CardTitle>{company.name}</CardTitle>
          <Badge variant={statusVariant[company.status ?? ''] ?? 'neutral'}>
            {statusLabel[company.status ?? ''] ?? company.status}
          </Badge>
        </CardHeader>
        {company.status !== 'APPROVED' && (
          <CardContent>
            {company.status === 'PENDING_APPROVAL' && (
              <Alert variant="warning">
                Tu empresa todavía no fue aprobada por un administrador. No podés crear Experiences hasta que se
                apruebe.
              </Alert>
            )}
            {company.status === 'REJECTED' && (
              <Alert variant="destructive">
                Tu solicitud fue rechazada{company.rejectionReason ? `: ${company.rejectionReason}` : '.'}
              </Alert>
            )}
            {company.status === 'SUSPENDED' && <Alert variant="destructive">Tu empresa fue suspendida por un administrador.</Alert>}
          </CardContent>
        )}
      </Card>

      {company.status === 'APPROVED' && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Editar perfil</CardTitle>
          </CardHeader>
          <CardContent>
            <form className="flex flex-col gap-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
              <FormField id="name" label="Nombre" error={errors.name?.message}>
                <Input {...fieldAria('name', errors.name?.message)} {...form.register('name')} />
              </FormField>
              <FormField
                id="description"
                label="Descripción (opcional)"
                error={errors.description?.message}
                counter={{ length: description.length, max: LIMITS.companyDescriptionMax }}
              >
                <Textarea {...fieldAria('description', errors.description?.message)} rows={3} {...form.register('description')} />
              </FormField>
              <FormField id="contactEmail" label="Correo de contacto" error={errors.contactEmail?.message}>
                <Input
                  {...fieldAria('contactEmail', errors.contactEmail?.message)}
                  type="email"
                  maxLength={LIMITS.emailMax}
                  {...form.register('contactEmail')}
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
              {submitError && <Alert variant="destructive">{submitError}</Alert>}
              {success && <Alert variant="success">Perfil actualizado.</Alert>}
              <Button type="submit" disabled={form.formState.isSubmitting} className="self-start">
                Guardar cambios
              </Button>
            </form>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
