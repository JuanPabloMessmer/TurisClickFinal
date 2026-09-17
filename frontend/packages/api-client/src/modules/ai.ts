import type { AxiosInstance } from 'axios'
import type {
  BookItineraryRequest,
  BookItineraryResponse,
  ConversationResponse,
  ConversationSummaryResponsePagedResult,
  ItemExplanationResponse,
  ItineraryResponse,
  SavedItinerarySummaryResponsePagedResult,
  SendMessageResponse,
} from '../types'

/**
 * Asistente de viajes (UC-T-12..18, UC-AI-01..06). Exclusivo TOURIST. El backend es la única fuente de
 * productos, precios y disponibilidad: el cliente nunca arma un itinerario por su cuenta.
 */

/** UC-T-12. */
export const createConversation = (http: AxiosInstance) =>
  http.post<ConversationResponse>('/api/ai/conversations').then((r) => r.data)

export const listMyConversations = (http: AxiosInstance, params?: { page?: number; pageSize?: number }) =>
  http.get<ConversationSummaryResponsePagedResult>('/api/ai/conversations/me', { params }).then((r) => r.data)

export const getConversation = (http: AxiosInstance, id: string) =>
  http.get<ConversationResponse>(`/api/ai/conversations/${id}`).then((r) => r.data)

/** UC-T-13/15 — un mensaje puede generar o ajustar la propuesta. Puede tardar: el backend compone y revalida. */
export const sendMessage = (http: AxiosInstance, conversationId: string, content: string) =>
  http.post<SendMessageResponse>(`/api/ai/conversations/${conversationId}/messages`, { content }).then((r) => r.data)

/** UC-T-14 — propuesta vigente, revalidada contra el catálogo. 404 si todavía no hay ninguna. */
export const getLatestItinerary = (http: AxiosInstance, conversationId: string) =>
  http.get<ItineraryResponse>(`/api/ai/conversations/${conversationId}/itinerary`).then((r) => r.data)

/** UC-T-17. */
export const listSavedItineraries = (http: AxiosInstance, params?: { page?: number; pageSize?: number }) =>
  http.get<SavedItinerarySummaryResponsePagedResult>('/api/ai/itineraries/me', { params }).then((r) => r.data)

export const getItinerary = (http: AxiosInstance, id: string) =>
  http.get<ItineraryResponse>(`/api/ai/itineraries/${id}`).then((r) => r.data)

/** UC-T-16 — guardar no reserva ni retiene cupos. Idempotente. */
export const saveItinerary = (http: AxiosInstance, id: string) =>
  http.post<ItineraryResponse>(`/api/ai/itineraries/${id}/save`).then((r) => r.data)

/** UC-T-18 — reserva atómica de todo el itinerario (queda PENDING_PAYMENT; el pago es el de siempre). */
export const bookItinerary = (http: AxiosInstance, id: string, body: BookItineraryRequest = {}) =>
  http.post<BookItineraryResponse>(`/api/ai/itineraries/${id}/book`, body).then((r) => r.data)

/** UC-AI-06 — por qué este componente está en el itinerario. */
export const getItemExplanation = (http: AxiosInstance, itineraryId: string, itemId: string) =>
  http.get<ItemExplanationResponse>(`/api/ai/itineraries/${itineraryId}/items/${itemId}/explanation`).then((r) => r.data)
