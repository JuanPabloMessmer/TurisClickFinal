import { fireEvent, render as rtlRender, screen, waitFor } from '@testing-library/react-native'
import ProfileScreen from '@/../app/(tabs)/profile'
import { useSession } from '@/auth/session'
import { createTestQueryClient, ok, withClient } from '@/test-utils'

/**
 * Perfil es la única puerta a lo privado en Fase 1, y la regla es que NO bloquea la app: sin sesión
 * ofrece entrar o registrarse, nunca un muro. Estos tests fijan esa diferencia.
 */

const mockPush = jest.fn()
jest.mock('expo-router', () => ({ useRouter: () => ({ push: mockPush, back: jest.fn() }) }))
const mockGet = jest.fn()
jest.mock('@/lib/httpClient', () => ({ httpClient: { get: (...args: unknown[]) => mockGet(...args) } }))

/** Perfil consulta las preferencias del turista: cada render lleva su propio QueryClient. */
const render = async (ui: React.ReactElement) => {
  const Wrapper = withClient(createTestQueryClient())
  return rtlRender(<Wrapper>{ui}</Wrapper>)
}
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

beforeEach(() => {
  jest.clearAllMocks()
  mockGet.mockReturnValue(ok({ categories: [], onboardingCompleted: false }))
})

describe('invitado', () => {
  beforeEach(() => givenSession({ status: 'unauthenticated' }))

  it('ofrece entrar y crear cuenta en vez de bloquear la pantalla', async () => {
    await render(<ProfileScreen />)

    expect(screen.getByText('Iniciar sesión')).toBeTruthy()
    expect(screen.getByText('Crear cuenta')).toBeTruthy()
  })

  it('aclara que se puede seguir explorando sin cuenta', async () => {
    await render(<ProfileScreen />)

    expect(screen.getByText(/seguir explorando/i)).toBeTruthy()
  })

  it('lleva a login y a registro', async () => {
    await render(<ProfileScreen />)

    await fireEvent.press(screen.getByText('Iniciar sesión'))
    expect(mockPush).toHaveBeenCalledWith('/(auth)/login')

    await fireEvent.press(screen.getByText('Crear cuenta'))
    expect(mockPush).toHaveBeenCalledWith('/(auth)/register')
  })

  it('no muestra cerrar sesión', async () => {
    await render(<ProfileScreen />)

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

  it('muestra los datos que el backend devuelve del usuario', async () => {
    await render(<ProfileScreen />)

    expect(screen.getByText('Ana Quispe')).toBeTruthy()
    expect(screen.getByText('ana@example.com')).toBeTruthy()
  })

  it('ofrece cerrar sesión y no los CTA de invitado', async () => {
    await render(<ProfileScreen />)

    expect(screen.getByText('Cerrar sesión')).toBeTruthy()
    expect(screen.queryByText('Iniciar sesión')).toBeNull()
    expect(screen.queryByText('Crear cuenta')).toBeNull()
  })

  it('cierra sesión al presionar', async () => {
    await render(<ProfileScreen />)

    await fireEvent.press(screen.getByText('Cerrar sesión'))

    expect(mockLogout).toHaveBeenCalled()
  })

  it('no ofrece editar los datos de la cuenta, porque no existe endpoint para eso', async () => {
    await render(<ProfileScreen />)

    expect(screen.queryByText(/editar/i)).toBeNull()
  })

  it('sin preferencias invita a completarlas y lleva al onboarding en modo edición', async () => {
    await render(<ProfileScreen />)

    await fireEvent.press(await screen.findByText('Completar mis preferencias'))

    expect(mockGet).toHaveBeenCalledWith('/api/tourists/me/preferences')
    expect(mockPush).toHaveBeenCalledWith({ pathname: '/onboarding', params: { mode: 'edit' } })
  })

  it('con preferencias muestra el resumen guardado', async () => {
    mockGet.mockReturnValue(
      ok({
        categories: [{ id: 'c1', name: 'Naturaleza' }, { id: 'c2', name: 'Gastronomía' }],
        travelPace: 'RELAXED',
        travelParty: 'COUPLE',
        budgetLevel: 'MODERATE',
        onboardingCompleted: true,
      }),
    )
    await render(<ProfileScreen />)

    await waitFor(() => expect(screen.getByText('Naturaleza')).toBeTruthy())
    expect(screen.getByText('Gastronomía')).toBeTruthy()
    expect(screen.getByText('Ritmo tranquilo · En pareja · Presupuesto moderado')).toBeTruthy()
    expect(screen.getByText('Ajustar preferencias')).toBeTruthy()
  })
})

describe('mientras se restaura la sesión', () => {
  it('no muestra ni los CTA de invitado ni los datos, para no parpadear', async () => {
    givenSession({ status: 'idle' })

    await render(<ProfileScreen />)

    expect(screen.queryByText('Iniciar sesión')).toBeNull()
    expect(screen.queryByText('Cerrar sesión')).toBeNull()
  })
})
