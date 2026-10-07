import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthBootstrap } from '@/auth/AuthBootstrap'
import { ChangePasswordPage } from '@/auth/ChangePasswordPage'
import { LoginPage } from '@/auth/LoginPage'
import { RequireRole } from '@/auth/RequireRole'
import { RootRedirect } from '@/auth/RootRedirect'
import { AppLayout } from '@/layout/AppLayout'
import { queryClient } from '@/lib/queryClient'
import { ExperienceAvailabilityPage } from '@/modules/availability/ExperienceAvailabilityPage'
import { CategoriesPage } from '@/modules/categories/CategoriesPage'
import { DashboardPage } from '@/modules/dashboard/DashboardPage'
import { CompaniesApprovalPage } from '@/modules/companies/CompaniesApprovalPage'
import { CompanyDetailPage } from '@/modules/companies/CompanyDetailPage'
import { NewProviderAccountPage } from '@/modules/companies/NewProviderAccountPage'
import { AdminExperiencesPage, AdminPackagesPage } from '@/modules/platform/AdminCatalogPages'
import { AdminReservationDetailPage } from '@/modules/platform/AdminReservationDetailPage'
import { AdminReservationsPage } from '@/modules/platform/AdminReservationsPage'
import { DestinationsPage } from '@/modules/destinations/DestinationsPage'
import { ExperienceFormPage } from '@/modules/experiences/ExperienceFormPage'
import { ExperiencesPage } from '@/modules/experiences/ExperiencesPage'
import { MyCompanyPage } from '@/modules/my-company/MyCompanyPage'
import { RequireApprovedCompany } from '@/modules/my-company/RequireApprovedCompany'
import { PackageAvailabilityPage } from '@/modules/package-availability/PackageAvailabilityPage'
import { PackageFormPage } from '@/modules/packages/PackageFormPage'
import { PackagesPage } from '@/modules/packages/PackagesPage'
import { ProviderReservationDetailPage } from '@/modules/provider-reservations/ProviderReservationDetailPage'
import { ProviderReservationsPage } from '@/modules/provider-reservations/ProviderReservationsPage'

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthBootstrap>
          <Routes>
            <Route path="/login" element={<LoginPage />} />

            {/* Único camino disponible para una cuenta con contraseña temporal: la API bloquea todo lo demás. */}
            <Route element={<RequireRole roles={['ADMIN', 'PROVIDER']} allowPasswordChangePending />}>
              <Route path="/cambiar-contrasena" element={<ChangePasswordPage />} />
            </Route>

            {/* "Hoy" es la pantalla de inicio de los dos roles, y no exige empresa aprobada:
                un operador en revision tambien necesita ver en que estado esta. */}
            <Route element={<RequireRole roles={['ADMIN', 'PROVIDER']} />}>
              <Route element={<AppLayout />}>
                <Route path="/dashboard" element={<DashboardPage />} />
              </Route>
            </Route>

            <Route element={<RequireRole roles={['ADMIN']} />}>
              <Route element={<AppLayout />}>
                <Route path="/admin/destinations" element={<DestinationsPage />} />
                <Route path="/admin/categories" element={<CategoriesPage />} />
                <Route path="/admin/companies" element={<CompaniesApprovalPage />} />
                <Route path="/admin/companies/new" element={<NewProviderAccountPage />} />
                <Route path="/admin/companies/:id" element={<CompanyDetailPage />} />
                <Route path="/admin/experiences" element={<AdminExperiencesPage />} />
                <Route path="/admin/packages" element={<AdminPackagesPage />} />
                <Route path="/admin/reservations" element={<AdminReservationsPage />} />
                <Route path="/admin/reservations/:id" element={<AdminReservationDetailPage />} />
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
                  <Route path="/provider/packages" element={<PackagesPage />} />
                  <Route path="/provider/packages/new" element={<PackageFormPage />} />
                  <Route path="/provider/packages/:id/edit" element={<PackageFormPage />} />
                  <Route path="/provider/packages/:id/availability" element={<PackageAvailabilityPage />} />
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
