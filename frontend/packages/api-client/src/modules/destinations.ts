import type { AxiosInstance } from 'axios'
import type {
  CreateDestinationRequest,
  DestinationResponse,
  PublicDestinationResponse,
  UpdateDestinationRequest,
  UploadedImageResponse,
} from '../types'

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

/**
 * UC-A-04 — sube la imagen de un destino (JPG, PNG o WebP, hasta 5 MB) y devuelve su URL pública HTTPS. No
 * modifica ningún destino: esa URL se manda después como `imageUrl` en el alta o la edición.
 * `onProgress` recibe 0–100 mientras se envía el archivo.
 */
export const uploadDestinationImage = (http: AxiosInstance, file: File, onProgress?: (percent: number) => void) => {
  const form = new FormData()
  form.append('file', file)
  return http
    .post<UploadedImageResponse>('/api/admin/destinations/images', form, {
      onUploadProgress: (event) => {
        if (onProgress && event.total) onProgress(Math.round((event.loaded / event.total) * 100))
      },
    })
    .then((r) => r.data)
}
