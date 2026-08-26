import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { getErrorMessage } from '@/lib/errors'
import { useAuth } from './useAuth'

const schema = z.object({
  email: z.string().email('Ingresá un email válido.'),
  password: z.string().min(1, 'La contraseña es requerida.'),
})

type FormValues = z.infer<typeof schema>

export function LoginPage() {
  const { status, user, login, logout } = useAuth()
  const location = useLocation()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const form = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { email: '', password: '' } })

  const onSubmit = async (values: FormValues) => {
    setSubmitError(null)
    try {
      const result = await login(values)
      if (result.user?.role === 'TOURIST') {
        // El Backoffice es exclusivo ADMIN/PROVIDER — RequireRole también bloquearía esto,
        // pero acá damos un mensaje explícito en vez de un simple rebote silencioso.
        await logout()
        setSubmitError('Esta cuenta es de Tourist. El Backoffice es exclusivo para ADMIN y PROVIDER.')
      }
    } catch (error) {
      setSubmitError(getErrorMessage(error))
    }
  }

  if (status === 'authenticated' && user && user.role !== 'TOURIST') {
    const home = user.role === 'ADMIN' ? '/admin/destinations' : '/provider/company'
    const target = (location.state as { from?: string } | null)?.from ?? home
    return <Navigate to={target} replace />
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

      <Card className="w-full max-w-sm">
        <CardContent className="pt-6">
          <div className="mb-5">
            <h1 className="text-lg font-semibold text-foreground">Iniciar sesión</h1>
            <p className="text-sm text-muted-foreground">Ingresá con tu cuenta de ADMIN o PROVIDER.</p>
          </div>
          <form className="flex flex-col gap-4" onSubmit={form.handleSubmit(onSubmit)} noValidate>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="email">Email</Label>
              <Input id="email" type="email" autoComplete="username" {...form.register('email')} />
              {form.formState.errors.email && (
                <p className="text-xs text-destructive">{form.formState.errors.email.message}</p>
              )}
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="password">Contraseña</Label>
              <Input id="password" type="password" autoComplete="current-password" {...form.register('password')} />
              {form.formState.errors.password && (
                <p className="text-xs text-destructive">{form.formState.errors.password.message}</p>
              )}
            </div>
            {submitError && <Alert variant="destructive">{submitError}</Alert>}
            <Button type="submit" disabled={form.formState.isSubmitting} className="mt-1">
              {form.formState.isSubmitting ? 'Ingresando…' : 'Ingresar'}
            </Button>
          </form>
          <p className="mt-5 text-center text-sm text-muted-foreground">
            ¿Sos un Provider nuevo?{' '}
            <Link to="/register-provider" className="font-medium text-primary underline-offset-4 hover:underline">
              Registrá tu empresa
            </Link>
          </p>
        </CardContent>
      </Card>
    </div>
  )
}
