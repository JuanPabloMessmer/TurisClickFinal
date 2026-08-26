import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { getErrorMessage } from '@/lib/errors'
import { useMyCompany, useUpdateMyCompany } from './api'

const schema = z.object({
  name: z.string().min(2).max(150),
  description: z.string().max(1000).optional().or(z.literal('')),
  contactEmail: z.string().email(),
  contactPhone: z.string().max(30).optional().or(z.literal('')),
})

type FormValues = z.infer<typeof schema>

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
    values: company
      ? {
          name: company.name ?? '',
          description: company.description ?? '',
          contactEmail: company.contactEmail ?? '',
          contactPhone: company.contactPhone ?? '',
        }
      : undefined,
  })

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
      setSubmitError(getErrorMessage(error))
    }
  }

  return (
    <div className="max-w-2xl">
      <PageHeader title="Mi Empresa" description="UC-P-02 — perfil de tu empresa." />

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
              <div className="flex flex-col gap-1.5">
                <Label>Nombre</Label>
                <Input {...form.register('name')} />
                {form.formState.errors.name && (
                  <p className="text-xs text-destructive">{form.formState.errors.name.message}</p>
                )}
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>Descripción</Label>
                <Input {...form.register('description')} />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>Email de contacto</Label>
                <Input type="email" {...form.register('contactEmail')} />
                {form.formState.errors.contactEmail && (
                  <p className="text-xs text-destructive">{form.formState.errors.contactEmail.message}</p>
                )}
              </div>
              <div className="flex flex-col gap-1.5">
                <Label>Teléfono de contacto</Label>
                <Input {...form.register('contactPhone')} />
              </div>
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
