import { Navigate, Outlet } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { useAuth } from './useAuth'

/** Guard de ruta por rol. Backoffice es exclusivo de ADMIN/PROVIDER — un TOURIST nunca pasa este gate. */
export function RequireRole({ roles }: { roles: Array<'ADMIN' | 'PROVIDER'> }) {
  const { status, user } = useAuth()

  if (status === 'idle' || status === 'loading') return <FullScreenSpinner />
  if (status === 'unauthenticated' || !user) return <Navigate to="/login" replace />
  if (!roles.includes(user.role as 'ADMIN' | 'PROVIDER')) return <Navigate to="/login" replace />

  return <Outlet />
}
