import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthBootstrap } from '@/auth/AuthBootstrap'
import { LoginPage } from '@/auth/LoginPage'
import { RegisterProviderPage } from '@/auth/RegisterProviderPage'
import { RequireRole } from '@/auth/RequireRole'
import { RootRedirect } from '@/auth/RootRedirect'
import { AppLayout } from '@/layout/AppLayout'
import { queryClient } from '@/lib/queryClient'
import { ExperienceAvailabilityPage } from '@/modules/availability/ExperienceAvailabilityPage'
import { CategoriesPage } from '@/modules/categories/CategoriesPage'
import { CompaniesApprovalPage } from '@/modules/companies/CompaniesApprovalPage'
import { DestinationsPage } from '@/modules/destinations/DestinationsPage'
import { ExperienceFormPage } from '@/modules/experiences/ExperienceFormPage'
import { ExperiencesPage } from '@/modules/experiences/ExperiencesPage'
import { MyCompanyPage } from '@/modules/my-company/MyCompanyPage'
import { RequireApprovedCompany } from '@/modules/my-company/RequireApprovedCompany'
import { ProviderReservationDetailPage } from '@/modules/provider-reservations/ProviderReservationDetailPage'
import { ProviderReservationsPage } from '@/modules/provider-reservations/ProviderReservationsPage'

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthBootstrap>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/register-provider" element={<RegisterProviderPage />} />

            <Route element={<RequireRole roles={['ADMIN']} />}>
              <Route element={<AppLayout />}>
                <Route path="/admin/destinations" element={<DestinationsPage />} />
                <Route path="/admin/categories" element={<CategoriesPage />} />
                <Route path="/admin/companies" element={<CompaniesApprovalPage />} />
              </Route>
            </Route>

            <Route element={<RequireRole roles={['PROVIDER']} />}>
              <Route element={<AppLayout />}>
                <Route path="/provider/company" element={<MyCompanyPage />} />
                <Route element={<RequireApprovedCompany />}>
                  <Route path="/provider/experiences" element={<ExperiencesPage />} />
                  <Route path="/provider/experiences/new" element={<ExperienceFormPage />} />
                  <Route path="/provider/experiences/:id/edit" element={<ExperienceFormPage />} />
                  <Route path="/provider/experiences/:id/availability" element={<ExperienceAvailabilityPage />} />
                  <Route path="/provider/reservations" element={<ProviderReservationsPage />} />
                  <Route path="/provider/reservations/:id" element={<ProviderReservationDetailPage />} />
                </Route>
              </Route>
            </Route>

            <Route path="/" element={<RootRedirect />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </AuthBootstrap>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
