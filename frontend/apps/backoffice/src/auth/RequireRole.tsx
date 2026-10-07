import { Navigate, Outlet } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { useAuth } from './useAuth'

/**
 * Guard de ruta por rol. Backoffice es exclusivo de ADMIN/PROVIDER — un TOURIST nunca pasa este gate.
 *
 * Además desvía a cambiar la contraseña cuando la cuenta todavía arrastra la temporal con la que un
 * administrador la creó. **No es la medida de seguridad**: la API rechaza esas operaciones por su cuenta. Esto
 * sólo evita que la persona se choque con errores sin entender por qué.
 */
export function RequireRole({
  roles,
  allowPasswordChangePending = false,
}: {
  roles: Array<'ADMIN' | 'PROVIDER'>
  allowPasswordChangePending?: boolean
}) {
  const { status, user } = useAuth()

  if (status === 'idle' || status === 'loading') return <FullScreenSpinner />
  if (status === 'unauthenticated' || !user) return <Navigate to="/login" replace />
  if (!roles.includes(user.role as 'ADMIN' | 'PROVIDER')) return <Navigate to="/login" replace />
  if (user.mustChangePassword && !allowPasswordChangePending)
    return <Navigate to="/cambiar-contrasena" replace />

  return <Outlet />
}
