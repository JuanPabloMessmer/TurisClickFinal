import { zodResolver } from '@hookform/resolvers/zod'
import { authApi } from '@turisclick/api-client'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { useState } from 'react'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { BrandMark } from '@/components/BrandMark'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { getErrorMessage } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'
import { authManager } from './authManager'
import { useAuth } from './useAuth'

/**
 * Cambio obligatorio de la contraseña con la que un administrador dio de alta la cuenta.
 *
 * No es un recordatorio amable: la API rechaza cualquier otra operación mientras la contraseña temporal siga
 * vigente, así que esta pantalla es literalmente lo único que la cuenta puede hacer. Se dice con esas
 * palabras en vez de dejar que la persona descubra el bloqueo chocándose con un error.
 */
const schema = z
  .object({
    currentPassword: z.string().min(1, 'Ingresá la contraseña que te enviaron.'),
    newPassword: z.string().min(10, 'Usá al menos 10 caracteres.'),
    confirmPassword: z.string().min(1, 'Repetí la contraseña nueva.'),
  })
  .refine((values) => values.newPassword === values.confirmPassword, {
    path: ['confirmPassword'],
    message: 'Las dos contraseñas tienen que coincidir.',
  })

type FormValues = z.infer<typeof schema>

export function ChangePasswordPage() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  })

  const onSubmit = async (values: FormValues) => {
    setSubmitError(null)
    try {
      const result = await authApi.changePassword(httpClient, {
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      })

      // El token nuevo ya no lleva el bloqueo: se hidrata la sesión con él y se entra al panel, sin pedirle
      // de nuevo la contraseña que la persona acaba de elegir.
      authManager.applyExternalSession(result)
      navigate('/dashboard', { replace: true })
    } catch (error) {
      setSubmitError(getErrorMessage(error))
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-background p-4">
      <Card className="w-full max-w-md">
        <CardHeader className="gap-3">
          <BrandMark subtitle="Primer ingreso" />
          <CardTitle className="text-heading">Elegí tu contraseña</CardTitle>
          <p className="text-body text-ink-muted">
            Entraste con una contraseña temporal. Para empezar a operar necesitás reemplazarla por una tuya.
          </p>
        </CardHeader>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
            {user?.email && (
              <div className="rounded-sm bg-muted p-3">
                <p className="text-label text-ink-muted">Tu cuenta</p>
                <p className="text-body font-medium text-foreground">{user.email}</p>
              </div>
            )}

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="currentPassword">Contraseña temporal</Label>
              <Input
                id="currentPassword"
                type="password"
                autoComplete="current-password"
                {...form.register('currentPassword')}
              />
              {form.formState.errors.currentPassword && (
                <p className="text-xs text-destructive">{form.formState.errors.currentPassword.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="newPassword">Contraseña nueva</Label>
              <Input id="newPassword" type="password" autoComplete="new-password" {...form.register('newPassword')} />
              <p className="text-caption text-ink-muted">Al menos 10 caracteres.</p>
              {form.formState.errors.newPassword && (
                <p className="text-xs text-destructive">{form.formState.errors.newPassword.message}</p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="confirmPassword">Repetí la contraseña nueva</Label>
              <Input
                id="confirmPassword"
                type="password"
                autoComplete="new-password"
                {...form.register('confirmPassword')}
              />
              {form.formState.errors.confirmPassword && (
                <p className="text-xs text-destructive">{form.formState.errors.confirmPassword.message}</p>
              )}
            </div>

            {submitError && <Alert variant="destructive">{submitError}</Alert>}

            <Button type="submit" loading={form.formState.isSubmitting} className="mt-1">
              Guardar y entrar
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
