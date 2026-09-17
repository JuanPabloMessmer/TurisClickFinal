import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  experiencesApi,
  type BulkCreateExperienceAvailabilityRequest,
  type CreateExperienceAvailabilityRequest,
  type UpdateAvailabilityRequest,
} from '@turisclick/api-client'
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

/** Alta masiva por calendario. Un dryRun no invalida nada: no escribió. */
export function useBulkCreateAvailability(experienceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: BulkCreateExperienceAvailabilityRequest) => experiencesApi.bulkCreateAvailability(httpClient, experienceId, body),
    onSuccess: (result) => {
      if (!result.dryRun) void queryClient.invalidateQueries({ queryKey: ['availability', experienceId] })
    },
  })
}

export function useUpdateAvailability(experienceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ availabilityId, body }: { availabilityId: string; body: UpdateAvailabilityRequest }) =>
      experiencesApi.updateAvailability(httpClient, experienceId, availabilityId, body),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['availability', experienceId] }),
  })
}
