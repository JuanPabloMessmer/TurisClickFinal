import { Navigate, Outlet } from 'react-router-dom'
import { FullScreenSpinner } from '@/components/FullScreenSpinner'
import { useMyCompany } from './api'

/** Gate para features de PROVIDER que exigen Company.Status = APPROVED (Experiences, Availability, Reservations). */
export function RequireApprovedCompany() {
  const { data, isLoading, isError } = useMyCompany()

  if (isLoading) return <FullScreenSpinner />
  if (isError || !data || data.status !== 'APPROVED') return <Navigate to="/provider/company" replace />

  return <Outlet />
}
