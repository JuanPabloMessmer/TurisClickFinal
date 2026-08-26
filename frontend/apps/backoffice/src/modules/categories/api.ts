import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { categoriesApi, type CreateCategoryRequest, type UpdateCategoryRequest } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

const KEY = ['categories'] as const

export function useCategories() {
  return useQuery({ queryKey: KEY, queryFn: () => categoriesApi.listCategories(httpClient) })
}

export function useCreateCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateCategoryRequest) => categoriesApi.createCategory(httpClient, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useUpdateCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateCategoryRequest }) => categoriesApi.updateCategory(httpClient, id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useDeleteCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => categoriesApi.deleteCategory(httpClient, id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}
