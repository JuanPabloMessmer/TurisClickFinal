import type { TokenStorage } from '@turisclick/auth-core'
import * as SecureStore from 'expo-secure-store'

const REFRESH_TOKEN_KEY = 'turisclick.refreshToken'

/**
 * Implementación móvil del TokenStorage de auth-core: Keychain en iOS y Keystore en Android, en vez
 * del localStorage que usa el Backoffice. El AuthManager no sabe cuál de las dos está usando.
 *
 * Las tres operaciones fallan en silencio a propósito. El AuthManager llama a `setRefreshToken` sin
 * esperar la promesa (`void this.storage.setRefreshToken(...)`), así que cualquier rechazo acá sería un
 * "unhandled promise rejection" que nadie puede atrapar. Y el almacén seguro puede no estar disponible
 * de verdad: en web no existe, y en un dispositivo el Keychain puede rechazar la escritura con la
 * pantalla bloqueada. La degradación correcta es quedarse sin "seguir conectado" —la sesión sigue viva
 * en memoria hasta cerrar la app— y no tumbar el login.
 */
export const secureTokenStorage: TokenStorage = {
  async getRefreshToken() {
    try {
      return await SecureStore.getItemAsync(REFRESH_TOKEN_KEY)
    } catch {
      // Un almacén corrupto o no disponible no puede tirar abajo el arranque: se trata como "sin sesión".
      return null
    }
  },

  async setRefreshToken(token: string) {
    try {
      await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, token)
    } catch {
      // Sin persistencia: la sesión vale solo para esta ejecución.
    }
  },

  async clearRefreshToken() {
    try {
      await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY)
    } catch {
      // Si no se pudo borrar es porque tampoco se pudo guardar: no hay nada que quede colgado.
    }
  },
}
