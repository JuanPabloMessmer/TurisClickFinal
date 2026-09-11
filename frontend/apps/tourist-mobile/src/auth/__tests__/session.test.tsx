import { act, render, screen, waitFor } from '@testing-library/react-native'
import * as SecureStore from 'expo-secure-store'
import { Text } from 'react-native'
import { NotATouristAccountError, SessionProvider, useSession } from '@/auth/session'

/**
 * El gating por rol se probó contra el AuthManager REAL: solo se mockea la capa HTTP. Así el test
 * verifica lo que importa de verdad — que una credencial válida de PROVIDER o ADMIN no deje sesión ni
 * tokens guardados en el dispositivo — y no que llamamos a un mock.
 */
const mockLogin = jest.fn()
const mockLogout = jest.fn().mockResolvedValue({})
const mockRefresh = jest.fn()
const mockRegisterTourist = jest.fn()

jest.mock('@turisclick/api-client', () => ({
  ...jest.requireActual('@turisclick/api-client'),
  authApi: {
    login: (...args: unknown[]) => mockLogin(...args),
    logout: (...args: unknown[]) => mockLogout(...args),
    refresh: (...args: unknown[]) => mockRefresh(...args),
    registerTourist: (...args: unknown[]) => mockRegisterTourist(...args),
  },
}))

function sessionFor(role: string) {
  return {
    accessToken: `access-${role}`,
    refreshToken: `refresh-${role}`,
    user: { id: 'u1', email: 'ana@example.com', fullName: 'Ana Quispe', role },
  }
}

let session: ReturnType<typeof useSession>

function Probe() {
  session = useSession()
  return <Text>{session.status}</Text>
}

const renderSession = () =>
  render(
    <SessionProvider>
      <Probe />
    </SessionProvider>,
  )

beforeEach(async () => {
  jest.clearAllMocks()
  await SecureStore.deleteItemAsync('turisclick.refreshToken')
})

describe('login', () => {
  it('deja la sesión iniciada para un TOURIST', async () => {
    mockLogin.mockResolvedValue(sessionFor('TOURIST'))
    renderSession()
    await waitFor(() => expect(screen.getByText('unauthenticated')).toBeTruthy())

    await act(async () => {
      await session.login('ana@example.com', 'secret123')
    })

    expect(session.isAuthenticated).toBe(true)
    expect(session.user?.role).toBe('TOURIST')
    expect(mockLogout).not.toHaveBeenCalled()
  })

  it.each(['PROVIDER', 'ADMIN'])('rechaza una cuenta %s con credenciales válidas', async (role) => {
    mockLogin.mockResolvedValue(sessionFor(role))
    renderSession()
    await waitFor(() => expect(screen.getByText('unauthenticated')).toBeTruthy())

    await act(async () => {
      await expect(session.login('otro@example.com', 'secret123')).rejects.toBeInstanceOf(
        NotATouristAccountError,
      )
    })

    expect(session.isAuthenticated).toBe(false)
    expect(session.user).toBeNull()
  })

  it('borra el refresh token del almacenamiento seguro al rechazar por rol', async () => {
    mockLogin.mockResolvedValue(sessionFor('PROVIDER'))
    renderSession()
    await waitFor(() => expect(screen.getByText('unauthenticated')).toBeTruthy())

    await act(async () => {
      await session.login('proveedor@example.com', 'secret123').catch(() => {})
    })

    await waitFor(async () => {
      expect(await SecureStore.getItemAsync('turisclick.refreshToken')).toBeNull()
    })
  })

  it('propaga el error del backend tal cual cuando las credenciales son inválidas', async () => {
    mockLogin.mockRejectedValue(new Error('401'))
    renderSession()
    await waitFor(() => expect(screen.getByText('unauthenticated')).toBeTruthy())

    await act(async () => {
      await expect(session.login('ana@example.com', 'mala')).rejects.not.toBeInstanceOf(
        NotATouristAccountError,
      )
    })

    expect(session.isAuthenticated).toBe(false)
  })
})

describe('register', () => {
  it('deja la sesión iniciada sin pedir un login extra', async () => {
    mockRegisterTourist.mockResolvedValue(sessionFor('TOURIST'))
    renderSession()
    await waitFor(() => expect(screen.getByText('unauthenticated')).toBeTruthy())

    await act(async () => {
      await session.register({
        firstName: 'Ana',
        lastName: 'Quispe',
        email: 'ana@example.com',
        password: 'secret123',
      })
    })

    expect(mockLogin).not.toHaveBeenCalled()
    expect(session.isAuthenticated).toBe(true)
  })
})

describe('bootstrap', () => {
  it('mantiene el status indeterminado hasta terminar, para no mandar a login a quien sí tenía sesión', async () => {
    await SecureStore.setItemAsync('turisclick.refreshToken', 'guardado')
    let resolveRefresh: (value: unknown) => void = () => {}
    mockRefresh.mockReturnValue(new Promise((resolve) => (resolveRefresh = resolve)))

    renderSession()

    expect(screen.getByText('idle')).toBeTruthy()

    await act(async () => {
      resolveRefresh(sessionFor('TOURIST'))
    })
    await waitFor(() => expect(session.isAuthenticated).toBe(true))
  })

  it('cierra la sesión restaurada si el token guardado no es de un TOURIST', async () => {
    await SecureStore.setItemAsync('turisclick.refreshToken', 'guardado-de-un-provider')
    mockRefresh.mockResolvedValue(sessionFor('PROVIDER'))

    renderSession()

    await waitFor(() => expect(screen.getByText('unauthenticated')).toBeTruthy())
    expect(session.isAuthenticated).toBe(false)
    expect(await SecureStore.getItemAsync('turisclick.refreshToken')).toBeNull()
  })

  it('sin token guardado queda desautenticado sin llamar al backend', async () => {
    renderSession()

    await waitFor(() => expect(screen.getByText('unauthenticated')).toBeTruthy())
    expect(mockRefresh).not.toHaveBeenCalled()
  })
})
