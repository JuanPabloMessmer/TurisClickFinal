import type { AxiosInstance } from 'axios'
import type { CategoryResponse, CreateCategoryRequest, UpdateCategoryRequest } from '../types'

/** UC-A-05 — exclusivo ADMIN. */
export const listCategories = (http: AxiosInstance) => http.get<CategoryResponse[]>('/api/admin/categories').then((r) => r.data)

export const createCategory = (http: AxiosInstance, body: CreateCategoryRequest) =>
  http.post<CategoryResponse>('/api/admin/categories', body).then((r) => r.data)

export const updateCategory = (http: AxiosInstance, id: string, body: UpdateCategoryRequest) =>
  http.put<CategoryResponse>(`/api/admin/categories/${id}`, body).then((r) => r.data)

export const deleteCategory = (http: AxiosInstance, id: string) => http.delete<void>(`/api/admin/categories/${id}`)
