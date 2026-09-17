import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import AssistantChatScreen from '@/../app/assistant/[id]'
import AssistantScreen from '@/../app/(tabs)/assistant'
import { useSession } from '@/auth/session'
import { createTestQueryClient, guestSession, httpError, ok, touristSession, withClient } from '@/test-utils'

const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))
const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }
jest.mock('expo-router', () => ({
  useRouter: () => mockRouter,
  useLocalSearchParams: () => ({ id: 'conv-1' }),
  Stack: { Screen: () => null },
  Link: ({ children }: { children: React.ReactNode }) => children,
}))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const itinerary = {
  id: 'it-1',
  aiConversationId: 'conv-1',
  title: 'Tu viaje a La Paz',
  status: 'DRAFT',
  version: 1,
  isStillBookable: true,
  items: [
    {
      id: 'item-1', dayNumber: 1, sortOrder: 0, productType: 'EXPERIENCE', experienceId: 'exp-1', experienceTitle: 'Tiwanaku y la Puerta del Sol',
      date: '2026-10-07', estimatedUnitPrice: 380, currency: 'BOB', travelers: 1, subtotal: 380, currentPrice: 380, currentCurrency: 'BOB',
      currentAvailableSlots: 16, availabilityState: 'AVAILABLE', isStillAvailable: true,
    },
    {
      id: 'item-2', dayNumber: 2, sortOrder: 0, productType: 'EXPERIENCE', experienceId: 'exp-2', experienceTitle: 'Chacaltaya a 5.300 m',
      date: '2026-10-08', estimatedUnitPrice: 320, currency: 'BOB', travelers: 1, subtotal: 320, currentPrice: 320, currentCurrency: 'BOB',
      currentAvailableSlots: 0, availabilityState: 'SOLD_OUT', isStillAvailable: false,
    },
  ],
  totals: [{ currency: 'BOB', amount: 700 }],
  warnings: [],
}

const conversation = {
  id: 'conv-1',
  status: 'ACTIVE',
  messages: [
    { id: 'm1', sender: 'TOURIST', content: 'Voy 2 días a La Paz' },
    { id: 'm2', sender: 'AI', content: 'Armé un itinerario combinando experiencias reales.' },
  ],
}

function backend(overrides: Record<string, unknown> = {}) {
  mockGet.mockImplementation((url: string) => {
    if (url in overrides) return overrides[url] as Promise<unknown>
    if (url === '/api/ai/conversations/conv-1') return ok(conversation)
    if (url === '/api/ai/conversations/conv-1/itinerary') return ok(itinerary)
    if (url === '/api/destinations') return ok([{ id: 'lpz', name: 'La Paz', publishedExperienceCount: 11 }])
    if (url.startsWith('/api/experiences/')) return ok({ id: url.split('/').pop(), destinationName: 'La Paz', images: [] })
    if (url === '/api/ai/itineraries/it-1/items/item-1/explanation') {
      return ok({ explanation: 'Te propuse Tiwanaku porque encaja con tus fechas.', facts: ['Cupos disponibles: 16.', 'Precio por persona: BOB 380.'] })
    }
    return ok({ items: [] })
  })
}

function renderChat() {
  const Wrapper = withClient(createTestQueryClient())
  return render(
    <Wrapper>
      <AssistantChatScreen />
    </Wrapper>,
  )
}

beforeEach(() => {
  jest.clearAllMocks()
  ;(useSession as jest.Mock).mockReturnValue(touristSession('u1', 'Ana Quispe'))
})

describe('chat del asistente', () => {
  it('muestra la conversación y el itinerario por días con productos, precios y disponibilidad reales', async () => {
    backend()
    renderChat()

    expect(await screen.findByText('Armé un itinerario combinando experiencias reales.')).toBeTruthy()
    expect(await screen.findByText('Tu viaje a La Paz')).toBeTruthy()
    expect(screen.getByText('Día 1')).toBeTruthy()
    expect(screen.getByText('Día 2')).toBeTruthy()
    expect(screen.getByText('Tiwanaku y la Puerta del Sol')).toBeTruthy()
    expect(screen.getByText('Disponible · 16 lugares')).toBeTruthy()
    expect(screen.getByText('Sin cupo')).toBeTruthy()
    expect(screen.getAllByText(/BOB\s+700\.00/).length).toBeGreaterThan(0)
    // Refinamientos rápidos cuando ya hay propuesta.
    expect(screen.getByLabelText('Quiero algo más barato')).toBeTruthy()
  })

  it('enviar un ajuste: aparece al instante, muestra las pistas del perfil y la nueva versión', async () => {
    const v2 = { ...itinerary, version: 2, title: 'Tu viaje a La Paz (más barato)' }
    let sent = false
    const afterSend = {
      ...conversation,
      messages: [
        ...conversation.messages,
        { id: 'm3', sender: 'TOURIST', content: 'Quiero algo más barato' },
        { id: 'm4', sender: 'AI', content: 'Actualicé tu itinerario.' },
      ],
    }
    backend({})
    const base = mockGet.getMockImplementation()!
    mockGet.mockImplementation((url: string) =>
      url === '/api/ai/conversations/conv-1' && sent ? ok(afterSend) : url === '/api/ai/conversations/conv-1/itinerary' && sent ? ok(v2) : base(url),
    )
    mockPost.mockImplementation(() => {
      sent = true
      return ok({ assistantMessage: 'Actualicé tu itinerario.', clarificationNeeded: false, itinerary: v2, warnings: [], profileHints: ['Tus intereses: Naturaleza', 'Viajás solo/a'] })
    })
    renderChat()
    await screen.findByText('Tu viaje a La Paz')

    fireEvent.press(screen.getByLabelText('Quiero algo más barato'))

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/ai/conversations/conv-1/messages', { content: 'Quiero algo más barato' }))
    expect(await screen.findByText('Actualicé tu itinerario.')).toBeTruthy()
    expect(screen.getAllByText('Quiero algo más barato').length).toBeGreaterThan(0)
    expect(await screen.findByText('Tu viaje a La Paz (más barato)')).toBeTruthy()
    expect(screen.getByText('Tus intereses: Naturaleza · Viajás solo/a')).toBeTruthy()
  })

  it('cuando falta información ofrece respuestas rápidas en vez de un formulario', async () => {
    backend({ '/api/ai/conversations/conv-1/itinerary': Promise.reject(httpError(404, { detail: 'Todavía no' })) })
    mockPost.mockReturnValue(ok({ assistantMessage: 'Necesito: destino', clarificationNeeded: true, missingInformation: ['destino'], warnings: [], profileHints: [] }))
    renderChat()
    await screen.findByText('Armé un itinerario combinando experiencias reales.')

    fireEvent.changeText(screen.getByLabelText('Mensaje para el asistente'), 'Quiero viajar')
    fireEvent.press(screen.getByLabelText('Enviar mensaje'))

    expect(await screen.findByLabelText('Quiero ir a La Paz')).toBeTruthy()
  })

  it('"¿Por qué?" abre la explicación con los hechos verificados', async () => {
    backend()
    renderChat()
    await screen.findByText('Tu viaje a La Paz')

    fireEvent.press(screen.getByLabelText('¿Por qué Tiwanaku y la Puerta del Sol?'))

    expect(await screen.findByText('Te propuse Tiwanaku porque encaja con tus fechas.')).toBeTruthy()
    expect(screen.getByText('✓ Cupos disponibles: 16.')).toBeTruthy()
  })

  it('guardar llama al backend y lo muestra guardado', async () => {
    backend()
    mockPost.mockReturnValue(ok({ ...itinerary, status: 'SAVED' }))
    renderChat()
    await screen.findByText('Tu viaje a La Paz')

    fireEvent.press(screen.getByText('Guardar para después'))

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/ai/itineraries/it-1/save'))
    expect(await screen.findByText('✓ Guardado en tus itinerarios')).toBeTruthy()
  })

  it('reservar con precios cambiados pide aceptarlos y recién después va al checkout', async () => {
    backend()
    mockPost
      .mockReturnValueOnce(
        ok({
          requiresPriceAcceptance: true,
          changes: [{ itineraryItemId: 'item-1', productTitle: 'Tiwanaku', previousCurrency: 'BOB', previousUnitPrice: 380, currentCurrency: 'BOB', currentUnitPrice: 400 }],
        }),
      )
      .mockReturnValueOnce(ok({ requiresPriceAcceptance: false, reservation: { id: 'res-9', status: 'PENDING_PAYMENT' } }))
    renderChat()
    await screen.findByText('Tu viaje a La Paz')

    fireEvent.press(screen.getByText('Reservar itinerario'))

    expect(await screen.findByText('Cambiaron algunos precios')).toBeTruthy()
    expect(screen.getByText('Tiwanaku: BOB 380.00 → BOB 400.00')).toBeTruthy()
    expect(mockRouter.push).not.toHaveBeenCalled()
    expect(mockPost).toHaveBeenLastCalledWith('/api/ai/itineraries/it-1/book', { acceptPriceChanges: false })

    fireEvent.press(screen.getByText('Aceptar precios y reservar'))

    await waitFor(() => expect(mockRouter.push).toHaveBeenCalledWith({ pathname: '/checkout/[id]', params: { id: 'res-9' } }))
    expect(mockPost).toHaveBeenLastCalledWith('/api/ai/itineraries/it-1/book', { acceptPriceChanges: true })
  })

  it('si el booking falla por cupo lo explica y no navega', async () => {
    backend()
    mockPost.mockRejectedValue(httpError(409, { errorCode: 'INSUFFICIENT_CAPACITY', detail: 'x' }))
    renderChat()
    await screen.findByText('Tu viaje a La Paz')

    await act(async () => fireEvent.press(screen.getByText('Reservar itinerario')))

    expect(await screen.findByText(/no se reservó nada/)).toBeTruthy()
    expect(mockRouter.push).not.toHaveBeenCalled()
  })
})

describe('pantalla Asistente', () => {
  function renderHome() {
    const Wrapper = withClient(createTestQueryClient())
    return render(
      <Wrapper>
        <AssistantScreen />
      </Wrapper>,
    )
  }

  it('sin sesión invita a iniciar sesión (AI Agent no es un rol de login)', () => {
    ;(useSession as jest.Mock).mockReturnValue(guestSession())
    backend()
    renderHome()

    expect(screen.getByText('Tu asistente de viaje')).toBeTruthy()
    expect(mockGet).not.toHaveBeenCalledWith('/api/ai/conversations/me', expect.anything())
  })

  it('una sugerencia crea la conversación, manda el mensaje y abre el chat', async () => {
    backend({ '/api/tourists/me/preferences': ok({ categories: [{ id: 'n', name: 'Naturaleza' }], travelPace: 'RELAXED', onboardingCompleted: true }) })
    mockPost.mockImplementation((url: string) =>
      url === '/api/ai/conversations'
        ? ok({ id: 'conv-new', status: 'ACTIVE', messages: [] })
        : ok({ assistantMessage: 'Listo', clarificationNeeded: false, itinerary, warnings: [], profileHints: [] }),
    )
    renderHome()

    expect(await screen.findByText('Parto de tu perfil')).toBeTruthy()
    fireEvent.press(screen.getByText('Voy 4 días a La Paz. Me gusta la naturaleza y la gastronomía.'))

    await waitFor(() => expect(mockRouter.push).toHaveBeenCalledWith({ pathname: '/assistant/[id]', params: { id: 'conv-new' } }))
    expect(mockPost).toHaveBeenCalledWith('/api/ai/conversations/conv-new/messages', {
      content: 'Voy 4 días a La Paz. Me gusta la naturaleza y la gastronomía.',
    })
  })
})
