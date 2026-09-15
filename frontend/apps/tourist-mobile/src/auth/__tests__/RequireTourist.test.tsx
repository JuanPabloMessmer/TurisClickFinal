import { fireEvent, render, screen } from '@testing-library/react-native'
import { Text } from 'react-native'
import { RequireTourist } from '@/auth/RequireTourist'
import { useSession } from '@/auth/session'
import { guestSession, sessionValue, touristSession } from '@/test-utils'

const mockPush = jest.fn()
jest.mock('expo-router', () => ({ useRouter: () => ({ push: mockPush }) }))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

/**
 * Fábrica y no una constante: si `rerender` recibe el MISMO elemento, React corta el re-render y el
 * componente nunca lee la sesión nueva. En la app real el cambio llega por contexto y sí re-renderiza.
 */
const guarded = () => (
  <RequireTourist title="Iniciá sesión para ver tus viajes" message="Tus reservas aparecen acá.">
    <Text>Contenido privado</Text>
  </RequireTourist>
)

beforeEach(() => jest.clearAllMocks())

it('con un turista autenticado muestra el contenido', () => {
  mockedUseSession.mockReturnValue(touristSession())
  render(guarded())

  expect(screen.getByText('Contenido privado')).toBeTruthy()
})

it('sin sesión NO muestra el contenido: invita a entrar sin redirigir', () => {
  mockedUseSession.mockReturnValue(guestSession())
  render(guarded())

  expect(screen.queryByText('Contenido privado')).toBeNull()
  expect(screen.getByText('Iniciá sesión para ver tus viajes')).toBeTruthy()

  fireEvent.press(screen.getByRole('button', { name: 'Iniciar sesión' }))
  expect(mockPush).toHaveBeenCalledWith('/(auth)/login')

  fireEvent.press(screen.getByRole('button', { name: 'Crear cuenta' }))
  expect(mockPush).toHaveBeenCalledWith('/(auth)/register')
})

it('mientras se restaura la sesión no muestra ni el contenido ni la invitación', () => {
  mockedUseSession.mockReturnValue(sessionValue({ status: 'idle' }))
  render(guarded())

  expect(screen.queryByText('Contenido privado')).toBeNull()
  expect(screen.queryByText('Iniciá sesión para ver tus viajes')).toBeNull()
})

it('si la sesión se pierde con la pantalla abierta (logout o refresh fallido) avisa que expiró', () => {
  mockedUseSession.mockReturnValue(touristSession())
  const { rerender } = render(guarded())
  expect(screen.getByText('Contenido privado')).toBeTruthy()

  mockedUseSession.mockReturnValue(guestSession())
  rerender(guarded())

  expect(screen.queryByText('Contenido privado')).toBeNull()
  expect(screen.getByText('Tu sesión expiró')).toBeTruthy()
})
