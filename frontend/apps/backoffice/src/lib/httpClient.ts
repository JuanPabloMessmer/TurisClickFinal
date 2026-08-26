import { createHttpClient } from '@turisclick/api-client'

const baseURL = import.meta.env.VITE_API_BASE_URL as string | undefined

if (!baseURL) {
  throw new Error('Falta VITE_API_BASE_URL — configurala en frontend/apps/backoffice/.env (ver .env.example).')
}

export const httpClient = createHttpClient(baseURL)
