import { preferencesApi, type UpdateTouristPreferencesRequest } from '@turisclick/api-client'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSession } from '@/auth/session'
import { httpClient } from '@/lib/httpClient'

/** Dato privado: la key lleva el id del turista y se borra al salir (ver clearPrivateQueries). */
export const preferenceKeys = {
  all: ['preferences'] as const,
  mine: (userId: string) => ['preferences', userId] as const,
}

export function useMyPreferences() {
  const { user, isAuthenticated } = useSession()
  const userId = user?.id ?? ''
  return useQuery({
    queryKey: preferenceKeys.mine(userId),
    queryFn: () => preferencesApi.getMyPreferences(httpClient),
    enabled: isAuthenticated && Boolean(userId),
  })
}

export function useSavePreferences() {
  const queryClient = useQueryClient()
  const { user } = useSession()
  return useMutation({
    mutationFn: (body: UpdateTouristPreferencesRequest) => preferencesApi.updateMyPreferences(httpClient, body),
    retry: false,
    onSuccess: (saved) => {
      if (user?.id) queryClient.setQueryData(preferenceKeys.mine(user.id), saved)
    },
  })
}
