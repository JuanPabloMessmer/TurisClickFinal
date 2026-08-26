import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { experiencesApi, type CreateExperienceAvailabilityRequest } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

export function useOwnedAvailability(experienceId: string) {
  return useQuery({
    queryKey: ['availability', experienceId],
    queryFn: () => experiencesApi.listOwnedAvailability(httpClient, experienceId),
  })
}

export function useCreateAvailability(experienceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateExperienceAvailabilityRequest) => experiencesApi.createAvailability(httpClient, experienceId, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['availability', experienceId] }),
  })
}
