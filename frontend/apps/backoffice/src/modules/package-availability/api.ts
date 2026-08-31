import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { packagesApi, type CreatePackageAvailabilityRequest } from '@turisclick/api-client'
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
