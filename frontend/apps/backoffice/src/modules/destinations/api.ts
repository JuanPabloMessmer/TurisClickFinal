import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { destinationsApi, type CreateDestinationRequest, type UpdateDestinationRequest } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

const KEY = ['destinations'] as const

export function useDestinations() {
  return useQuery({ queryKey: KEY, queryFn: () => destinationsApi.listDestinations(httpClient) })
}

export function useCreateDestination() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateDestinationRequest) => destinationsApi.createDestination(httpClient, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useUpdateDestination() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateDestinationRequest }) =>
      destinationsApi.updateDestination(httpClient, id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useDeleteDestination() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => destinationsApi.deleteDestination(httpClient, id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

/** Sube el archivo y devuelve la URL pública; el destino se guarda después con el POST/PUT de siempre. */
export function uploadDestinationImage(file: File, onProgress?: (percent: number) => void) {
  return destinationsApi.uploadDestinationImage(httpClient, file, onProgress)
}
