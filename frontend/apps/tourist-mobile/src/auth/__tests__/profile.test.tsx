import { fireEvent, render, screen } from '@testing-library/react-native'
import ProfileScreen from '@/../app/(tabs)/profile'
import { useSession } from '@/auth/session'

/**
 * Perfil es la única puerta a lo privado en Fase 1, y la regla es que NO bloquea la app: sin sesión
 * ofrece entrar o registrarse, nunca un muro. Estos tests fijan esa diferencia.
 */

const mockPush = jest.fn()
jest.mock('expo-router', () => ({ useRouter: () => ({ push: mockPush, back: jest.fn() }) }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>
const mockLogout = jest.fn()

function givenSession(overrides: Partial<ReturnType<typeof useSession>>) {
  mockedUseSession.mockReturnValue({
    status: 'unauthenticated',
    user: null,
    isAuthenticated: false,
    login: jest.fn(),
    register: jest.fn(),
    logout: mockLogout,
    ...overrides,
  })
}

beforeEach(() => jest.clearAllMocks())

describe('invitado', () => {
  beforeEach(() => givenSession({ status: 'unauthenticated' }))

  it('ofrece entrar y crear cuenta en vez de bloquear la pantalla', () => {
    render(<ProfileScreen />)

    expect(screen.getByText('Iniciar sesión')).toBeTruthy()
    expect(screen.getByText('Crear cuenta')).toBeTruthy()
  })

  it('aclara que se puede seguir explorando sin cuenta', () => {
    render(<ProfileScreen />)

    expect(screen.getByText(/seguir explorando/i)).toBeTruthy()
  })

  it('lleva a login y a registro', () => {
    render(<ProfileScreen />)

    fireEvent.press(screen.getByText('Iniciar sesión'))
    expect(mockPush).toHaveBeenCalledWith('/(auth)/login')

    fireEvent.press(screen.getByText('Crear cuenta'))
    expect(mockPush).toHaveBeenCalledWith('/(auth)/register')
  })

  it('no muestra cerrar sesión', () => {
    render(<ProfileScreen />)

    expect(screen.queryByText('Cerrar sesión')).toBeNull()
  })
})

describe('turista autenticado', () => {
  beforeEach(() =>
    givenSession({
      status: 'authenticated',
      isAuthenticated: true,
      user: { id: 'u1', firstName: 'Ana', fullName: 'Ana Quispe', email: 'ana@example.com', role: 'TOURIST' },
    }),
  )

  it('muestra los datos que el backend devuelve del usuario', () => {
    render(<ProfileScreen />)

    expect(screen.getByText('Ana Quispe')).toBeTruthy()
    expect(screen.getByText('ana@example.com')).toBeTruthy()
  })

  it('ofrece cerrar sesión y no los CTA de invitado', () => {
    render(<ProfileScreen />)

    expect(screen.getByText('Cerrar sesión')).toBeTruthy()
    expect(screen.queryByText('Iniciar sesión')).toBeNull()
    expect(screen.queryByText('Crear cuenta')).toBeNull()
  })

  it('cierra sesión al presionar', () => {
    render(<ProfileScreen />)

    fireEvent.press(screen.getByText('Cerrar sesión'))

    expect(mockLogout).toHaveBeenCalled()
  })

  it('no ofrece editar el perfil, porque no existe endpoint para eso', () => {
    render(<ProfileScreen />)

    expect(screen.queryByText(/editar/i)).toBeNull()
  })
})

describe('mientras se restaura la sesión', () => {
  it('no muestra ni los CTA de invitado ni los datos, para no parpadear', () => {
    givenSession({ status: 'idle' })

    render(<ProfileScreen />)

    expect(screen.queryByText('Iniciar sesión')).toBeNull()
    expect(screen.queryByText('Cerrar sesión')).toBeNull()
  })
})
