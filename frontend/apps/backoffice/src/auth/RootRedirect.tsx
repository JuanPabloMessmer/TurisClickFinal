import { Navigate } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { useAuth } from './useAuth'

export function RootRedirect() {
  const { status, user } = useAuth()

  if (status === 'idle' || status === 'loading') return <FullScreenSpinner />
  if (status !== 'authenticated' || !user) return <Navigate to="/login" replace />

  return <Navigate to={user.role === 'ADMIN' ? '/admin/destinations' : '/provider/company'} replace />
}
