import type { AxiosInstance } from 'axios'
import type { CreateDestinationRequest, DestinationResponse, PublicDestinationResponse, UpdateDestinationRequest } from '../types'

/** UC-T-03 — público (AllowAnonymous), lo usa también el Provider para elegir el destino de una Experience. */
export const listPublicDestinations = (http: AxiosInstance, params?: { parentId?: string; type?: string }) =>
  http.get<PublicDestinationResponse[]>('/api/destinations', { params }).then((r) => r.data)

/** UC-A-04 — exclusivo ADMIN. */
export const listDestinations = (http: AxiosInstance, params?: { parentId?: string; type?: string }) =>
  http.get<DestinationResponse[]>('/api/admin/destinations', { params }).then((r) => r.data)

export const getDestination = (http: AxiosInstance, id: string) =>
  http.get<DestinationResponse>(`/api/admin/destinations/${id}`).then((r) => r.data)

export const createDestination = (http: AxiosInstance, body: CreateDestinationRequest) =>
  http.post<DestinationResponse>('/api/admin/destinations', body).then((r) => r.data)

export const updateDestination = (http: AxiosInstance, id: string, body: UpdateDestinationRequest) =>
  http.put<DestinationResponse>(`/api/admin/destinations/${id}`, body).then((r) => r.data)

export const deleteDestination = (http: AxiosInstance, id: string) => http.delete<void>(`/api/admin/destinations/${id}`)
