import { Navigate } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { useAuth } from './useAuth'

export function RootRedirect() {
  const { status, user } = useAuth()

  if (status === 'idle' || status === 'loading') return <FullScreenSpinner />
  if (status !== 'authenticated' || !user) return <Navigate to="/login" replace />

  // Una cuenta recién creada por un administrador no puede hacer nada hasta cambiar su contraseña: se la
  // lleva directo ahí en vez de dejarla rebotar contra errores.
  if (user.mustChangePassword) return <Navigate to="/cambiar-contrasena" replace />

  // Los dos roles aterrizan en "Hoy": antes el ADMIN caia en una tabla de destinos y el PROVIDER en
  // el formulario de su empresa, y ninguno veia el trabajo pendiente.
  return <Navigate to="/dashboard" replace />
}
