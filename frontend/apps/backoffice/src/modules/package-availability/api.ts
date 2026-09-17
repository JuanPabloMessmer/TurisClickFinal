import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  packagesApi,
  type BulkCreatePackageAvailabilityRequest,
  type CreatePackageAvailabilityRequest,
  type UpdateAvailabilityRequest,
} from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

export function useOwnedPackageAvailability(packageId: string) {
  return useQuery({
    queryKey: ['packageAvailability', packageId],
    queryFn: () => packagesApi.listOwnedPackageAvailability(httpClient, packageId),
  })
}

export function useCreatePackageAvailability(packageId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreatePackageAvailabilityRequest) => packagesApi.createPackageAvailability(httpClient, packageId, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['packageAvailability', packageId] }),
  })
}

export function useBulkCreatePackageAvailability(packageId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: BulkCreatePackageAvailabilityRequest) => packagesApi.bulkCreatePackageAvailability(httpClient, packageId, body),
    onSuccess: (result) => {
      if (!result.dryRun) void queryClient.invalidateQueries({ queryKey: ['packageAvailability', packageId] })
    },
  })
}

export function useUpdatePackageAvailability(packageId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ availabilityId, body }: { availabilityId: string; body: UpdateAvailabilityRequest }) =>
      packagesApi.updatePackageAvailability(httpClient, packageId, availabilityId, body),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['packageAvailability', packageId] }),
  })
}
