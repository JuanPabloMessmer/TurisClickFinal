import { AuthManager } from '@turisclick/auth-core'
import { httpClient } from '@/lib/httpClient'
import { localStorageTokenStorage } from '@/lib/tokenStorage'

/** Instancia única para toda la app — un solo AxiosInstance, un solo store de sesión. */
export const authManager = new AuthManager(httpClient, localStorageTokenStorage)
