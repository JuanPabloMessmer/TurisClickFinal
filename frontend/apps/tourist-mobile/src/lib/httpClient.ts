import { createHttpClient } from '@turisclick/api-client'
import { API_BASE_URL, API_TIMEOUT_MS } from './env'

/** Una sola instancia para toda la app: es la que el AuthManager decora con sus interceptores. */
export const httpClient = createHttpClient(API_BASE_URL, { timeoutMs: API_TIMEOUT_MS })
