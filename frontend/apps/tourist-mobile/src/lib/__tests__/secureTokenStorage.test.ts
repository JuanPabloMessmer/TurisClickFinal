import * as SecureStore from 'expo-secure-store'
import { secureTokenStorage } from '@/lib/secureTokenStorage'

/**
 * El AuthManager llama a `setRefreshToken` sin esperar la promesa, así que un rechazo acá se convierte
 * en un unhandled rejection que nadie puede atrapar. Estos tests fijan que las tres operaciones
 * absorban el fallo del almacén seguro.
 */

const mockedStore = SecureStore as jest.Mocked<typeof SecureStore>

// El mock de jest.setup.js es un almacén en memoria compartido por todo el archivo: hay que vaciarlo.
beforeEach(async () => {
  await secureTokenStorage.clearRefreshToken()
  jest.clearAllMocks()
})

it('guarda y recupera el refresh token', async () => {
  await secureTokenStorage.setRefreshToken('token-123')

  expect(await secureTokenStorage.getRefreshToken()).toBe('token-123')
})

it('devuelve null cuando no hay nada guardado', async () => {
  expect(await secureTokenStorage.getRefreshToken()).toBeNull()
})

it('borra el token', async () => {
  await secureTokenStorage.setRefreshToken('token-123')

  await secureTokenStorage.clearRefreshToken()

  expect(await secureTokenStorage.getRefreshToken()).toBeNull()
})

it('trata un almacén ilegible como "sin sesión" en vez de propagar el error', async () => {
  mockedStore.getItemAsync.mockRejectedValueOnce(new Error('keychain locked'))

  await expect(secureTokenStorage.getRefreshToken()).resolves.toBeNull()
})

it('no rechaza si el almacén no puede escribir', async () => {
  mockedStore.setItemAsync.mockRejectedValueOnce(new Error('SecureStore no disponible'))

  await expect(secureTokenStorage.setRefreshToken('token-123')).resolves.toBeUndefined()
})

it('no rechaza si el almacén no puede borrar', async () => {
  mockedStore.deleteItemAsync.mockRejectedValueOnce(new Error('SecureStore no disponible'))

  await expect(secureTokenStorage.clearRefreshToken()).resolves.toBeUndefined()
})
