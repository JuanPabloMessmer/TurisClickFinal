import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { useSession } from '@/auth/session'
import { OnboardingFlow } from '@/features/preferences/OnboardingFlow'
import { createTestQueryClient, httpError, ok, touristSession, withClient } from '@/test-utils'

const mockGet = jest.fn()
const mockPut = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), put: (...args: unknown[]) => mockPut(...args) },
}))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const categories = [
  { id: 'nat', name: 'Naturaleza' },
  { id: 'gas', name: 'Gastronomía' },
  { id: 'cul', name: 'Cultura' },
]

function backend(saved: object = { categories: [], onboardingCompleted: false }) {
  mockGet.mockImplementation((url: string) => (url === '/api/categories' ? ok(categories) : ok(saved)))
}

function ui(props: Partial<React.ComponentProps<typeof OnboardingFlow>> = {}) {
  const Wrapper = withClient(createTestQueryClient())
  return (
    <Wrapper>
      <OnboardingFlow mode="onboarding" firstName="Ana" onDone={jest.fn()} {...props} />
    </Wrapper>
  )
}

const next = () => fireEvent.press(screen.getByText('Siguiente'))

beforeEach(() => {
  jest.clearAllMocks()
  ;(useSession as jest.Mock).mockReturnValue(touristSession('u1', 'Ana Quispe'))
  mockPut.mockImplementation((_url: string, body: object) => ok({ ...body, onboardingCompleted: true }))
})

it('recorre las 4 preguntas y guarda exactamente lo elegido', async () => {
  backend()
  const onDone = jest.fn()
  render(ui({ onDone }))

  expect(screen.getByText('¡Bienvenido/a, Ana!')).toBeTruthy()
  expect(screen.getByText('¿Qué te gusta hacer cuando viajás?')).toBeTruthy()
  fireEvent.press(await screen.findByLabelText('Naturaleza'))
  fireEvent.press(screen.getByLabelText('Gastronomía'))
  expect(screen.getByLabelText('Naturaleza').props.accessibilityState).toMatchObject({ checked: true })
  next()

  expect(screen.getByText('¿Cómo te gusta viajar?')).toBeTruthy()
  fireEvent.press(screen.getByLabelText(/^Tranquilo/))
  next()

  expect(screen.getByText('¿Con quién viajás normalmente?')).toBeTruthy()
  fireEvent.press(screen.getByLabelText(/^En pareja/))
  next()

  expect(screen.getByText('¿Cuánto preferís gastar?')).toBeTruthy()
  fireEvent.press(screen.getByLabelText(/^Moderado/))
  next()

  expect(screen.getByText('Tu perfil de viaje')).toBeTruthy()
  expect(screen.getByText('Naturaleza, Gastronomía')).toBeTruthy()
  expect(screen.getByText('🌿 Tranquilo')).toBeTruthy()
  expect(mockPut).not.toHaveBeenCalled() // nada se guarda antes de confirmar

  fireEvent.press(screen.getByText('Empezar a explorar'))

  await waitFor(() => expect(onDone).toHaveBeenCalled())
  expect(mockPut).toHaveBeenCalledWith('/api/tourists/me/preferences', {
    categoryIds: ['nat', 'gas'],
    travelPace: 'RELAXED',
    travelParty: 'COUPLE',
    budgetLevel: 'MODERATE',
    completeOnboarding: true,
  })
})

it('se puede saltear: marca el onboarding como hecho con lo elegido hasta ahí', async () => {
  backend()
  const onDone = jest.fn()
  render(ui({ onDone }))

  fireEvent.press(await screen.findByLabelText('Cultura'))
  fireEvent.press(screen.getByText('Saltar'))

  await waitFor(() => expect(onDone).toHaveBeenCalled())
  expect(mockPut).toHaveBeenCalledWith('/api/tourists/me/preferences', expect.objectContaining({ categoryIds: ['cul'], completeOnboarding: true }))
})

it('desde el resumen se puede volver a cambiar una respuesta', async () => {
  backend()
  render(ui())
  await screen.findByLabelText('Cultura')
  next()
  next()
  next()
  next()

  fireEvent.press(screen.getByLabelText(/^Cambiar Ritmo/))

  expect(screen.getByText('¿Cómo te gusta viajar?')).toBeTruthy()
})

it('en edición arranca con lo guardado, no ofrece saltear y guarda los cambios', async () => {
  backend({ categories: [{ id: 'gas', name: 'Gastronomía' }], travelPace: 'INTENSE', travelParty: 'SOLO', budgetLevel: null, onboardingCompleted: true })
  const onDone = jest.fn()
  render(ui({ mode: 'edit', onDone }))

  await waitFor(() => expect(screen.getByLabelText('Gastronomía').props.accessibilityState).toMatchObject({ checked: true }))
  expect(screen.queryByText('Saltar')).toBeNull()
  fireEvent.press(screen.getByLabelText('Gastronomía')) // lo quita
  next()
  expect(screen.getByLabelText(/^Intenso/).props.accessibilityState).toMatchObject({ selected: true })
  next()
  next()
  next()

  fireEvent.press(screen.getByText('Guardar cambios'))

  await waitFor(() => expect(onDone).toHaveBeenCalled())
  expect(mockPut).toHaveBeenCalledWith('/api/tourists/me/preferences', {
    categoryIds: [],
    travelPace: 'INTENSE',
    travelParty: 'SOLO',
    budgetLevel: null,
    completeOnboarding: true,
  })
})

it('si falla el guardado lo dice y no cierra', async () => {
  backend()
  mockPut.mockRejectedValue(httpError(400, { detail: 'Alguno de los intereses elegidos no existe.' }))
  const onDone = jest.fn()
  render(ui({ onDone }))
  await screen.findByLabelText('Cultura')
  next()
  next()
  next()
  next()

  fireEvent.press(screen.getByText('Empezar a explorar'))

  await waitFor(() => expect(screen.getByText('Alguno de los intereses elegidos no existe.')).toBeTruthy())
  expect(onDone).not.toHaveBeenCalled()
})
