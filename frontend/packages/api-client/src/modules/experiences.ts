import type { AxiosInstance } from 'axios'
import type {
  CreateExperienceAvailabilityRequest,
  CreateExperienceRequest,
  ExperienceAvailabilityResponse,
  ExperienceResponse,
  ExperienceSummaryResponsePagedResult,
  UpdateExperienceRequest,
} from '../types'

/** UC-P-04 — exclusivo PROVIDER (crea para su propia empresa, resuelta server-side). */
export const createExperience = (http: AxiosInstance, body: CreateExperienceRequest) =>
  http.post<ExperienceResponse>('/api/experiences', body).then((r) => r.data)

/** UC-P-05. */
export const updateExperience = (http: AxiosInstance, id: string, body: UpdateExperienceRequest) =>
  http.put<ExperienceResponse>(`/api/experiences/${id}`, body).then((r) => r.data)

/** UC-P-06. */
export const publishExperience = (http: AxiosInstance, id: string) =>
  http.post<ExperienceResponse>(`/api/experiences/${id}/publish`).then((r) => r.data)

export const unpublishExperience = (http: AxiosInstance, id: string) =>
  http.post<ExperienceResponse>(`/api/experiences/${id}/unpublish`).then((r) => r.data)

/** "Mis experiencias" — cualquier estado, exclusivo PROVIDER dueño. */
export const listMyExperiences = (http: AxiosInstance, params?: { page?: number; pageSize?: number }) =>
  http.get<ExperienceSummaryResponsePagedResult>('/api/experiences/mine', { params }).then((r) => r.data)

export const getMyExperienceById = (http: AxiosInstance, id: string) =>
  http.get<ExperienceResponse>(`/api/experiences/mine/${id}`).then((r) => r.data)

/** UC-P-10 — un slot por request. */
export const createAvailability = (http: AxiosInstance, experienceId: string, body: CreateExperienceAvailabilityRequest) =>
  http.post<ExperienceAvailabilityResponse>(`/api/experiences/${experienceId}/availability`, body).then((r) => r.data)

/** Vista de gestión del PROVIDER dueño — todos los slots, cualquier fecha/estado. */
export const listOwnedAvailability = (http: AxiosInstance, experienceId: string) =>
  http.get<ExperienceAvailabilityResponse[]>(`/api/experiences/mine/${experienceId}/availability`).then((r) => r.data)
