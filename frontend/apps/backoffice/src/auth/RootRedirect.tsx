import { Navigate } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { useAuth } from './useAuth'

export function RootRedirect() {
  const { status, user } = useAuth()

  if (status === 'idle' || status === 'loading') return <FullScreenSpinner />
  if (status !== 'authenticated' || !user) return <Navigate to="/login" replace />

  // Los dos roles aterrizan en "Hoy": antes el ADMIN caia en una tabla de destinos y el PROVIDER en
  // el formulario de su empresa, y ninguno veia el trabajo pendiente.
  return <Navigate to="/dashboard" replace />
}
