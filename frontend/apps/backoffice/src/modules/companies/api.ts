import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { companiesApi, type RejectCompanyRequest } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

const KEY = ['adminCompanies'] as const

export function useCompanies(status?: string) {
  return useQuery({
    queryKey: [...KEY, status ?? 'ALL'],
    queryFn: () => companiesApi.listCompanies(httpClient, { status, page: 1, pageSize: 100 }),
  })
}

export function useApproveCompany() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => companiesApi.approveCompany(httpClient, id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useRejectCompany() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: RejectCompanyRequest }) => companiesApi.rejectCompany(httpClient, id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}
