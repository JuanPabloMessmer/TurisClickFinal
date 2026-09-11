import { zodResolver } from '@hookform/resolvers/zod'
import { useRouter } from 'expo-router'
import { Controller, useForm } from 'react-hook-form'
import { useState } from 'react'
import { z } from 'zod'
import { AuthScreenShell, AuthSwitchLink } from '@/auth/AuthScreenShell'
import { NotATouristAccountError, useSession } from '@/auth/session'
import { toApiError } from '@/lib/errors'
import { useCloseModal } from '@/lib/navigation'
import { Button, FormError, TextField } from '@/ui'

const schema = z.object({
  email: z.email('Ingresá un email válido'),
  password: z.string().min(1, 'Ingresá tu contraseña'),
})

type LoginForm = z.infer<typeof schema>

export default function LoginScreen() {
  const router = useRouter()
  const closeModal = useCloseModal()
  const { login } = useSession()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const { control, handleSubmit, formState } = useForm<LoginForm>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '' },
  })

  const onSubmit = handleSubmit(async (values) => {
    setSubmitError(null)
    try {
      await login(values.email.trim(), values.password)
      closeModal()
    } catch (error) {
      // El rechazo por rol no es un fallo de la API: tiene su propio mensaje y no pasa por toApiError.
      setSubmitError(
        error instanceof NotATouristAccountError ? error.message : toApiError(error).message,
      )
    }
  })

  return (
    <AuthScreenShell
      title="Iniciar sesión"
      subtitle="Entrá para guardar tus datos y, muy pronto, reservar."
      footer={
        <AuthSwitchLink
          prompt="¿No tenés cuenta?"
          action="Creá una"
          onPress={() => router.replace('/(auth)/register')}
        />
      }
    >
      <FormError message={submitError} />

      <Controller
        control={control}
        name="email"
        render={({ field, fieldState }) => (
          <TextField
            label="Email"
            placeholder="tu@email.com"
            autoCapitalize="none"
            autoComplete="email"
            keyboardType="email-address"
            value={field.value}
            onChangeText={field.onChange}
            onBlur={field.onBlur}
            error={fieldState.error?.message}
          />
        )}
      />

      <Controller
        control={control}
        name="password"
        render={({ field, fieldState }) => (
          <TextField
            label="Contraseña"
            placeholder="••••••••"
            secureTextEntry
            autoCapitalize="none"
            autoComplete="current-password"
            value={field.value}
            onChangeText={field.onChange}
            onBlur={field.onBlur}
            onSubmitEditing={onSubmit}
            returnKeyType="go"
            error={fieldState.error?.message}
          />
        )}
      />

      <Button label="Entrar" loading={formState.isSubmitting} onPress={onSubmit} />
    </AuthScreenShell>
  )
}
