import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  destinationsApi,
  experiencesApi,
  type CreateExperienceRequest,
  type UpdateExperienceRequest,
} from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

const LIST_KEY = ['myExperiences'] as const

export function useMyExperiences() {
  return useQuery({
    queryKey: LIST_KEY,
    queryFn: () => experiencesApi.listMyExperiences(httpClient, { page: 1, pageSize: 100 }),
  })
}

export function useMyExperience(id: string | undefined) {
  return useQuery({
    queryKey: ['myExperience', id],
    queryFn: () => experiencesApi.getMyExperienceById(httpClient, id!),
    enabled: !!id,
  })
}

/** Destinos tipo CITY — únicos válidos para asociar a una Experience (regla de negocio del backend). */
export function useCityDestinations() {
  return useQuery({
    queryKey: ['publicDestinations', 'CITY'],
    queryFn: () => destinationsApi.listPublicDestinations(httpClient, { type: 'CITY' }),
  })
}

export function useCreateExperience() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateExperienceRequest) => experiencesApi.createExperience(httpClient, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: LIST_KEY }),
  })
}

export function useUpdateExperience() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateExperienceRequest }) =>
      experiencesApi.updateExperience(httpClient, id, body),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: LIST_KEY })
      queryClient.invalidateQueries({ queryKey: ['myExperience', variables.id] })
    },
  })
}

export function usePublishExperience() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => experiencesApi.publishExperience(httpClient, id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: LIST_KEY }),
  })
}

export function useUnpublishExperience() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => experiencesApi.unpublishExperience(httpClient, id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: LIST_KEY }),
  })
}
