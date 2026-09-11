/* eslint-env jest */

// expo-secure-store es un módulo nativo: en Jest no existe, así que se sustituye por un almacén en
// memoria. Es suficiente para lo que probamos (que los tokens se guarden y se borren al hacer logout).
jest.mock('expo-secure-store', () => {
  const store = new Map()
  return {
    getItemAsync: jest.fn(async (key) => store.get(key) ?? null),
    setItemAsync: jest.fn(async (key, value) => void store.set(key, value)),
    deleteItemAsync: jest.fn(async (key) => void store.delete(key)),
  }
})

jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { hostUri: 'localhost:8081', version: '1.0.0' } },
}))

// Las safe areas las mide el nativo; sin provider los hooks tiran error. La librería publica su propio
// mock con insets fijos, que es exactamente lo que hace falta para renderizar componentes sueltos.
// El mock publica todo bajo "default", así que hay que desenvolverlo para que los imports nombrados funcionen.
jest.mock('react-native-safe-area-context', () => require('react-native-safe-area-context/jest/mock').default)
