import {
  aiApi,
  type ConversationResponse,
  type ItineraryResponse,
  type SendMessageResponse,
} from '@turisclick/api-client'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSession } from '@/auth/session'
import { reservationKeys } from '@/features/reservations/keys'
import { toApiError } from '@/lib/errors'
import { httpClient } from '@/lib/httpClient'

/**
 * Datos del asistente. Todo es privado del turista: las keys cuelgan de `['ai', userId]`, que se borra al
 * salir (clearPrivateQueries). Las pantallas no hablan con axios: pantalla → hook → api-client → backend.
 */
export const aiKeys = {
  scope: (userId: string) => ['ai', userId] as const,
  conversations: (userId: string) => ['ai', userId, 'conversations'] as const,
  conversation: (userId: string, id: string) => ['ai', userId, 'conversation', id] as const,
  latestItinerary: (userId: string, conversationId: string) => ['ai', userId, 'conversation', conversationId, 'itinerary'] as const,
  saved: (userId: string) => ['ai', userId, 'saved'] as const,
  itinerary: (userId: string, id: string) => ['ai', userId, 'itinerary', id] as const,
  explanation: (userId: string, itineraryId: string, itemId: string) => ['ai', userId, 'itinerary', itineraryId, 'explanation', itemId] as const,
  /** Última respuesta del asistente en esta sesión (pistas del perfil, datos faltantes, avisos): no se persiste en el backend. */
  lastReply: (userId: string, conversationId: string) => ['ai', userId, 'conversation', conversationId, 'lastReply'] as const,
}

/** La última respuesta conocida de una conversación (solo caché local; null si no hubo envío en esta sesión). */
export function useLastReply(conversationId: string) {
  const userId = useTouristId()
  const queryClient = useQueryClient()
  return useQuery({
    queryKey: aiKeys.lastReply(userId ?? 'anonymous', conversationId),
    queryFn: () => queryClient.getQueryData<SendMessageResponse>(aiKeys.lastReply(userId ?? 'anonymous', conversationId)) ?? null,
    enabled: userId !== null && Boolean(conversationId),
    staleTime: Infinity,
  })
}

function useTouristId(): string | null {
  const { isAuthenticated, user } = useSession()
  return isAuthenticated && user?.id ? user.id : null
}

export function useConversations() {
  const userId = useTouristId()
  return useQuery({
    queryKey: aiKeys.conversations(userId ?? 'anonymous'),
    queryFn: () => aiApi.listMyConversations(httpClient, { page: 1, pageSize: 10 }),
    enabled: userId !== null,
  })
}

export function useSavedItineraries() {
  const userId = useTouristId()
  return useQuery({
    queryKey: aiKeys.saved(userId ?? 'anonymous'),
    queryFn: () => aiApi.listSavedItineraries(httpClient, { page: 1, pageSize: 10 }),
    enabled: userId !== null,
  })
}

export function useConversation(id: string) {
  const userId = useTouristId()
  return useQuery({
    queryKey: aiKeys.conversation(userId ?? 'anonymous', id),
    queryFn: () => aiApi.getConversation(httpClient, id),
    enabled: userId !== null && Boolean(id),
  })
}

/** La propuesta vigente, revalidada por el backend. Todavía no haber propuesta (404) no es un error: es null. */
export function useLatestItinerary(conversationId: string) {
  const userId = useTouristId()
  return useQuery({
    queryKey: aiKeys.latestItinerary(userId ?? 'anonymous', conversationId),
    queryFn: async (): Promise<ItineraryResponse | null> => {
      try {
        return await aiApi.getLatestItinerary(httpClient, conversationId)
      } catch (error) {
        if (toApiError(error).status === 404) return null
        throw error
      }
    },
    enabled: userId !== null && Boolean(conversationId),
  })
}

export function useItinerary(id: string) {
  const userId = useTouristId()
  return useQuery({
    queryKey: aiKeys.itinerary(userId ?? 'anonymous', id),
    queryFn: () => aiApi.getItinerary(httpClient, id),
    enabled: userId !== null && Boolean(id),
    // Retomar un itinerario guardado siempre revalida precios y cupos.
    staleTime: 0,
  })
}

export function useItemExplanation(itineraryId: string, itemId: string | null) {
  const userId = useTouristId()
  return useQuery({
    queryKey: aiKeys.explanation(userId ?? 'anonymous', itineraryId, itemId ?? ''),
    queryFn: () => aiApi.getItemExplanation(httpClient, itineraryId, itemId ?? ''),
    enabled: userId !== null && Boolean(itineraryId) && Boolean(itemId),
  })
}

/** Crea una conversación (y opcionalmente manda el primer mensaje). */
export function useStartConversation() {
  const queryClient = useQueryClient()
  const userId = useTouristId()
  return useMutation({
    retry: false,
    mutationFn: async (firstMessage?: string) => {
      const conversation = await aiApi.createConversation(httpClient)
      const reply = firstMessage ? await aiApi.sendMessage(httpClient, conversation.id ?? '', firstMessage) : null
      return { conversation, reply }
    },
    onSuccess: ({ conversation, reply }) => {
      if (!userId || !conversation.id) return
      void queryClient.invalidateQueries({ queryKey: aiKeys.conversations(userId) })
      if (reply) queryClient.setQueryData(aiKeys.lastReply(userId, conversation.id), reply)
      if (reply?.itinerary) queryClient.setQueryData(aiKeys.latestItinerary(userId, conversation.id), reply.itinerary)
    },
  })
}

/**
 * Enviar un mensaje. El mensaje del turista aparece al instante (optimista); la respuesta real del
 * asistente llega con la conversación re-leída. Nunca se reintenta solo: cada envío puede generar una
 * versión nueva del itinerario.
 */
export function useSendMessage(conversationId: string) {
  const queryClient = useQueryClient()
  const userId = useTouristId()
  return useMutation({
    retry: false,
    mutationFn: (content: string) => aiApi.sendMessage(httpClient, conversationId, content),
    onMutate: (content) => {
      if (!userId) return
      queryClient.setQueryData<ConversationResponse>(aiKeys.conversation(userId, conversationId), (current) =>
        current
          ? {
              ...current,
              messages: [
                ...(current.messages ?? []),
                { id: `pending-${Date.now()}`, sender: 'TOURIST', content, createdAt: new Date().toISOString() },
              ],
            }
          : current,
      )
    },
    onSuccess: (reply: SendMessageResponse) => {
      if (!userId) return
      queryClient.setQueryData(aiKeys.lastReply(userId, conversationId), reply)
      if (reply.itinerary) queryClient.setQueryData(aiKeys.latestItinerary(userId, conversationId), reply.itinerary)
      void queryClient.invalidateQueries({ queryKey: aiKeys.conversations(userId) })
    },
    onSettled: () => {
      if (userId) void queryClient.invalidateQueries({ queryKey: aiKeys.conversation(userId, conversationId) })
    },
  })
}

export function useSaveItinerary() {
  const queryClient = useQueryClient()
  const userId = useTouristId()
  return useMutation({
    retry: false,
    mutationFn: (itineraryId: string) => aiApi.saveItinerary(httpClient, itineraryId),
    onSuccess: (saved) => {
      if (!userId) return
      if (saved.id) queryClient.setQueryData(aiKeys.itinerary(userId, saved.id), saved)
      if (saved.aiConversationId) queryClient.setQueryData(aiKeys.latestItinerary(userId, saved.aiConversationId), saved)
      void queryClient.invalidateQueries({ queryKey: aiKeys.saved(userId) })
    },
  })
}

/** Reserva atómica del itinerario. Sin reintentos: tomar cupo dos veces no debe ser posible desde acá. */
export function useBookItinerary() {
  const queryClient = useQueryClient()
  const userId = useTouristId()
  return useMutation({
    retry: false,
    mutationFn: ({ itineraryId, acceptPriceChanges }: { itineraryId: string; acceptPriceChanges: boolean }) =>
      aiApi.bookItinerary(httpClient, itineraryId, { acceptPriceChanges }),
    onSettled: () => {
      if (!userId) return
      void queryClient.invalidateQueries({ queryKey: aiKeys.scope(userId) })
      void queryClient.invalidateQueries({ queryKey: reservationKeys.scope(userId) })
      void queryClient.invalidateQueries({
        predicate: (query) => query.queryKey[0] === 'catalog' && query.queryKey[query.queryKey.length - 1] === 'availability',
      })
    },
  })
}
