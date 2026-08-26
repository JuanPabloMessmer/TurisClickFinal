import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { companiesApi, type RejectCompanyRequest } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

const LIST_KEY = ['adminCompanies'] as const

export function useCompanies(params: { status?: string; search?: string; page: number; pageSize: number }) {
  return useQuery({
    queryKey: [...LIST_KEY, params],
    queryFn: () => companiesApi.listCompanies(httpClient, params),
    placeholderData: (previous) => previous,
  })
}

export function useCompany(id: string | undefined) {
  return useQuery({
    queryKey: ['adminCompany', id],
    queryFn: () => companiesApi.getCompanyById(httpClient, id!),
    enabled: !!id,
  })
}

export function useApproveCompany() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => companiesApi.approveCompany(httpClient, id),
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: LIST_KEY })
      queryClient.setQueryData(['adminCompany', data.id], data)
    },
  })
}

export function useRejectCompany() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: RejectCompanyRequest }) => companiesApi.rejectCompany(httpClient, id, body),
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: LIST_KEY })
      queryClient.setQueryData(['adminCompany', data.id], data)
    },
  })
}
