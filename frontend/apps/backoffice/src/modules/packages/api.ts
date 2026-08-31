import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  destinationsApi,
  packagesApi,
  type CreatePackageRequest,
  type UpdatePackageRequest,
} from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

const LIST_KEY = ['myPackages'] as const

export function useMyPackages() {
  return useQuery({
    queryKey: LIST_KEY,
    queryFn: () => packagesApi.listMyPackages(httpClient, { page: 1, pageSize: 100 }),
  })
}

export function useMyPackage(id: string | undefined) {
  return useQuery({
    queryKey: ['myPackage', id],
    queryFn: () => packagesApi.getMyPackageById(httpClient, id!),
    enabled: !!id,
  })
}

/** Destinos tipo CITY — únicos válidos para asociar a un Package (regla de negocio del backend). */
export function useCityDestinations() {
  return useQuery({
    queryKey: ['publicDestinations', 'CITY'],
    queryFn: () => destinationsApi.listPublicDestinations(httpClient, { type: 'CITY' }),
  })
}

export function useCreatePackage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreatePackageRequest) => packagesApi.createPackage(httpClient, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: LIST_KEY }),
  })
}

export function useUpdatePackage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdatePackageRequest }) => packagesApi.updatePackage(httpClient, id, body),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: LIST_KEY })
      queryClient.invalidateQueries({ queryKey: ['myPackage', variables.id] })
    },
  })
}

export function usePublishPackage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => packagesApi.publishPackage(httpClient, id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: LIST_KEY }),
  })
}

export function useUnpublishPackage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => packagesApi.unpublishPackage(httpClient, id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: LIST_KEY }),
  })
}
