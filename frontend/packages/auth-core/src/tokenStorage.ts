/**
 * Cada app implementa su propio storage seguro: Backoffice envuelve localStorage, Tourist Mobile
 * envolverá expo-secure-store. La lógica de auth (AuthManager) no sabe ni le importa cuál es.
 */
export interface TokenStorage {
  getRefreshToken(): Promise<string | null>
  setRefreshToken(token: string): Promise<void>
  clearRefreshToken(): Promise<void>
}
