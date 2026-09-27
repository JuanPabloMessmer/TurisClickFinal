/**
 * Única fuente de configuración del Backoffice (espejo de `src/lib/env.ts` en Tourist Mobile).
 *
 * Por defecto apunta al backend V2 desplegado en Azure, que es contra lo que corre el Backoffice
 * publicado. Para desarrollo local se pone `VITE_API_TARGET=local` (o una URL puntual con
 * `VITE_API_BASE_URL`) en `frontend/apps/backoffice/.env`.
 *
 * `VITE_*` queda embebido en el bundle: acá nunca van secretos. La sesión se obtiene con el login real
 * del backend y el refresh token vive en localStorage del navegador, no en el build.
 */
export const AZURE_API_BASE_URL = 'https://app-turisclick-v2-api.azurewebsites.net'
export const LOCAL_API_BASE_URL = 'http://localhost:5288'

export type ApiTarget = 'azure' | 'local' | 'custom'

export interface ApiConfig {
  target: ApiTarget
  baseUrl: string
}

/** Resuelve la configuración a partir de las variables públicas; `baseUrl` explícito gana siempre. */
export function resolveApiConfig(env: { target?: string; baseUrl?: string }): ApiConfig {
  const baseUrl = env.baseUrl?.trim()
  if (baseUrl) return { target: 'custom', baseUrl: baseUrl.replace(/\/+$/, '') }
  return env.target?.trim().toLowerCase() === 'local'
    ? { target: 'local', baseUrl: LOCAL_API_BASE_URL }
    : { target: 'azure', baseUrl: AZURE_API_BASE_URL }
}

export const API_CONFIG = resolveApiConfig({
  target: import.meta.env.VITE_API_TARGET as string | undefined,
  baseUrl: import.meta.env.VITE_API_BASE_URL as string | undefined,
})

export const API_BASE_URL = API_CONFIG.baseUrl

export const API_TARGET_LABEL: Record<ApiTarget, string> = {
  azure: 'Azure (V2)',
  local: 'local',
  custom: 'personalizado',
}

/**
 * El plan F1 de App Service se duerme sin uso: el primer request después de un rato puede tardar varios
 * segundos. Mismo criterio que Tourist Mobile — esperar es mejor que mostrar un error de conexión falso.
 */
export const API_TIMEOUT_MS = 60_000

export const APP_VERSION = '0.1.0'
