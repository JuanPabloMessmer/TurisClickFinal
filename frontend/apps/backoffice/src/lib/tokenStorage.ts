import type { TokenStorage } from '@turisclick/auth-core'

const STORAGE_KEY = 'turisclick.backoffice.refreshToken'

/**
 * Web: localStorage. Trade-off consciente y documentado (docs/frontend-architecture.md) — el backend
 * hoy devuelve el refresh token en el body del login, no como cookie httpOnly, así que esta es la opción
 * más simple sin tocar el contrato de Oleada 0. Tourist Mobile usará expo-secure-store en su lugar.
 */
export const localStorageTokenStorage: TokenStorage = {
  async getRefreshToken() {
    return localStorage.getItem(STORAGE_KEY)
  },
  async setRefreshToken(token: string) {
    localStorage.setItem(STORAGE_KEY, token)
  },
  async clearRefreshToken() {
    localStorage.removeItem(STORAGE_KEY)
  },
}
