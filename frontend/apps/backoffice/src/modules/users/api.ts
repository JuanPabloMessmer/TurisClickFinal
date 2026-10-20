import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { httpClient } from '@/lib/httpClient'

/** UC-A-06 — las cuentas de toda la plataforma, de cualquier rol. */

export const usersKeys = {
  all: ['admin', 'users'] as const,
  list: (params: adminApi.ListUsersParams) => ['admin', 'users', params] as const,
}

export function useAdminUsers(params: adminApi.ListUsersParams) {
  return useQuery({
    queryKey: usersKeys.list(params),
    queryFn: () => adminApi.listUsers(httpClient, params),
    placeholderData: (previous) => previous,
  })
}

/** Suspender bloquea el ingreso y la renovación de sesión; activar lo devuelve. */
export function useChangeUserStatus() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'suspend' | 'activate' }) =>
      action === 'suspend' ? adminApi.suspendUser(httpClient, id) : adminApi.activateUser(httpClient, id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: usersKeys.all })
      // La pantalla de administradores lee el mismo endpoint filtrado por rol.
      void queryClient.invalidateQueries({ queryKey: ['admin', 'administrators'] })
    },
  })
}
