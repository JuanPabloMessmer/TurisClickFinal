import { createHttpClient } from '@turisclick/api-client'
import { API_BASE_URL, API_TIMEOUT_MS } from '@/lib/env'

/** Un solo AxiosInstance para toda la app; la URL sale de `lib/env.ts` (Azure V2 por defecto). */
export const httpClient = createHttpClient(API_BASE_URL, { timeoutMs: API_TIMEOUT_MS })
