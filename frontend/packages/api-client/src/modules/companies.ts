import type { AxiosInstance } from 'axios'
import type {
  CompanyResponse,
  CompanyResponsePagedResult,
  RegisterProviderRequest,
  RegisterProviderResponse,
  RejectCompanyRequest,
  UpdateCompanyRequest,
} from '../types'

/** UC-P-01 — público, sin auth. */
export const registerProvider = (http: AxiosInstance, body: RegisterProviderRequest) =>
  http.post<RegisterProviderResponse>('/api/providers/register', body).then((r) => r.data)

/**
 * UC-A-01 — exclusivo ADMIN. status es opcional (sin filtro trae todas). search es opcional (coincidencia
 * parcial en Name/LegalDocument/ContactEmail).
 */
export const listCompanies = (
  http: AxiosInstance,
  params?: { status?: string; search?: string; page?: number; pageSize?: number },
) => http.get<CompanyResponsePagedResult>('/api/admin/companies', { params }).then((r) => r.data)

/** UC-A-01 — detalle de cualquier empresa (cualquier estado), exclusivo ADMIN. */
export const getCompanyById = (http: AxiosInstance, id: string) =>
  http.get<CompanyResponse>(`/api/admin/companies/${id}`).then((r) => r.data)

/** UC-A-02 — exclusivo ADMIN. */
export const approveCompany = (http: AxiosInstance, id: string) =>
  http.post<CompanyResponse>(`/api/admin/companies/${id}/approve`).then((r) => r.data)

/** UC-A-03 — exclusivo ADMIN. */
export const rejectCompany = (http: AxiosInstance, id: string, body: RejectCompanyRequest) =>
  http.post<CompanyResponse>(`/api/admin/companies/${id}/reject`, body).then((r) => r.data)

/** UC-P-02 — "Mi Empresa", exclusivo PROVIDER (su propia empresa). */
export const getMyCompany = (http: AxiosInstance) => http.get<CompanyResponse>('/api/companies/me').then((r) => r.data)

export const updateMyCompany = (http: AxiosInstance, body: UpdateCompanyRequest) =>
  http.put<CompanyResponse>('/api/companies/me', body).then((r) => r.data)
