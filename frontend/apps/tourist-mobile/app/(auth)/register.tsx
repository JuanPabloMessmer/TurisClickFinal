import { zodResolver } from '@hookform/resolvers/zod'
import { useRouter } from 'expo-router'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { AuthScreenShell, AuthSwitchLink } from '@/auth/AuthScreenShell'
import { NotATouristAccountError, useSession } from '@/auth/session'
import { toApiError } from '@/lib/errors'
import { useCloseModal } from '@/lib/navigation'
import { Button, FormError, TextField } from '@/ui'

/** Los límites replican exactamente los de RegisterTouristRequest en el backend. */
const schema = z.object({
  firstName: z.string().trim().min(2, 'Al menos 2 caracteres').max(100, 'Máximo 100 caracteres'),
  lastName: z.string().trim().min(2, 'Al menos 2 caracteres').max(100, 'Máximo 100 caracteres'),
  email: z.email('Ingresá un email válido').max(255, 'Máximo 255 caracteres'),
  password: z.string().min(8, 'Al menos 8 caracteres').max(100, 'Máximo 100 caracteres'),
})

type RegisterForm = z.infer<typeof schema>

export default function RegisterScreen() {
  const router = useRouter()
  const closeModal = useCloseModal()
  const { register } = useSession()
  const [submitError, setSubmitError] = useState<string | null>(null)

  const { control, handleSubmit, formState } = useForm<RegisterForm>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: '', lastName: '', email: '', password: '' },
  })

  const onSubmit = handleSubmit(async (values) => {
    setSubmitError(null)
    try {
      await register(values)
      closeModal()
    } catch (error) {
      setSubmitError(
        error instanceof NotATouristAccountError ? error.message : toApiError(error).message,
      )
    }
  })

  return (
    <AuthScreenShell
      title="Crear cuenta"
      subtitle="Es gratis y te toma menos de un minuto."
      footer={
        <AuthSwitchLink prompt="¿Ya tenés cuenta?" action="Iniciá sesión" onPress={() => router.replace('/(auth)/login')} />
      }
    >
      <FormError message={submitError} />

      <Controller
        control={control}
        name="firstName"
        render={({ field, fieldState }) => (
          <TextField
            label="Nombre"
            placeholder="Ana"
            autoComplete="given-name"
            value={field.value}
            onChangeText={field.onChange}
            onBlur={field.onBlur}
            error={fieldState.error?.message}
          />
        )}
      />

      <Controller
        control={control}
        name="lastName"
        render={({ field, fieldState }) => (
          <TextField
            label="Apellido"
            placeholder="Quispe"
            autoComplete="family-name"
            value={field.value}
            onChangeText={field.onChange}
            onBlur={field.onBlur}
            error={fieldState.error?.message}
          />
        )}
      />

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
            placeholder="Mínimo 8 caracteres"
            secureTextEntry
            autoCapitalize="none"
            autoComplete="new-password"
            value={field.value}
            onChangeText={field.onChange}
            onBlur={field.onBlur}
            onSubmitEditing={onSubmit}
            returnKeyType="go"
            error={fieldState.error?.message}
          />
        )}
      />

      <Button label="Crear cuenta" loading={formState.isSubmitting} onPress={onSubmit} />
    </AuthScreenShell>
  )
}
