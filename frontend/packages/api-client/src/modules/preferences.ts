import type { AxiosInstance } from 'axios'
import type { TouristPreferencesResponse, UpdateTouristPreferencesRequest } from '../types'

/** Perfil de viaje del turista autenticado (onboarding). Nunca 404: sin datos devuelve un perfil vacío. */
export const getMyPreferences = (http: AxiosInstance) =>
  http.get<TouristPreferencesResponse>('/api/tourists/me/preferences').then((r) => r.data)

/** Reemplazo completo. `completeOnboarding: true` al terminar o saltear el onboarding. */
export const updateMyPreferences = (http: AxiosInstance, body: UpdateTouristPreferencesRequest) =>
  http.put<TouristPreferencesResponse>('/api/tourists/me/preferences', body).then((r) => r.data)
