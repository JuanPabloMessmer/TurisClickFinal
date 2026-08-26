import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { companiesApi, type UpdateCompanyRequest } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

export function useMyCompany() {
  return useQuery({ queryKey: ['myCompany'], queryFn: () => companiesApi.getMyCompany(httpClient) })
}

export function useUpdateMyCompany() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: UpdateCompanyRequest) => companiesApi.updateMyCompany(httpClient, body),
    onSuccess: (data) => queryClient.setQueryData(['myCompany'], data),
  })
}
