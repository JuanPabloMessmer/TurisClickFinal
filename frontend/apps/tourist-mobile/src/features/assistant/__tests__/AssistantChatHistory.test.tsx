import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import AssistantChatScreen from '@/../app/assistant/[id]'
import { useSession } from '@/auth/session'
import { createTestQueryClient, httpError, ok, touristSession, withClient } from '@/test-utils'

/**
 * El historial del asistente, turno por turno. Encontrado probando en un iPhone: al pedir un ajuste ("Menos
 * aventura"), las recomendaciones de la respuesta anterior desaparecían y sólo quedaba la última propuesta.
 * Cada respuesta tiene que conservar sus propias tarjetas, en orden, al recargar y cuando algo falla.
 */
const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))
jest.mock('expo-router', () => ({
  useRouter: () => ({ push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }),
  useLocalSearchParams: () => ({ id: 'conv-1' }),
  Stack: { Screen: () => null },
  Link: ({ children }: { children: React.ReactNode }) => children,
}))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const isoDaysFromToday = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10)

function proposal(id: string, version: number, title: string, experienceTitle: string) {
  return {
    id,
    aiConversationId: 'conv-1',
    title,
    status: 'DRAFT',
    version,
    isStillBookable: true,
    items: [
      {
        id: `${id}-item`, dayNumber: 1, sortOrder: 0, productType: 'EXPERIENCE', experienceId: `${id}-exp`, experienceTitle,
        date: isoDaysFromToday(30), estimatedUnitPrice: 300, currency: 'BOB', travelers: 1, subtotal: 300,
        currentPrice: 300, currentCurrency: 'BOB', currentAvailableSlots: 8, availabilityState: 'AVAILABLE', isStillAvailable: true,
      },
    ],
    totals: [{ currency: 'BOB', amount: 300 }],
    warnings: [],
  }
}

const ADVENTURE = proposal('it-1', 1, 'Bolivia con aventura', 'Rafting en el río Coroico')
const CALMER = proposal('it-2', 2, 'Bolivia más tranquila', 'Museo de Etnografía y Folklore')

const FIRST_TURN = [
  { id: 'm1', sender: 'TOURIST', content: 'Quiero viajar por Bolivia y me gusta la aventura.' },
  { id: 'm2', sender: 'AI', content: 'Te armé una propuesta con aventura.', itineraryId: 'it-1' },
]
const SECOND_TURN = [
  { id: 'm3', sender: 'TOURIST', content: 'Menos aventura.' },
  { id: 'm4', sender: 'AI', content: 'Bajé la intensidad de la propuesta.', itineraryId: 'it-2' },
]

function conversationWith(messages: unknown[]) {
  return { id: 'conv-1', status: 'ACTIVE', messages }
}

/** El backend: la conversación, la propuesta vigente y cada versión por su id. */
function backend({
  messages,
  latest,
  overrides = {},
}: {
  messages: () => unknown[]
  latest: () => unknown
  overrides?: Record<string, () => Promise<unknown>>
}) {
  mockGet.mockImplementation((url: string) => {
    if (url in overrides) return overrides[url]()
    if (url === '/api/ai/conversations/conv-1') return ok(conversationWith(messages()))
    if (url === '/api/ai/conversations/conv-1/itinerary') return ok(latest())
    if (url === '/api/ai/itineraries/it-1') return ok(ADVENTURE)
    if (url === '/api/ai/itineraries/it-2') return ok(CALMER)
    if (url === '/api/destinations') return ok([{ id: 'lpz', name: 'La Paz', publishedExperienceCount: 11 }])
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

/** El texto de la pantalla en el orden en que se dibuja, para poder afirmar qué va debajo de qué. */
function renderedTextOrder(): string[] {
  const out: string[] = []
  const walk = (node: unknown) => {
    if (node == null) return
    if (typeof node === 'string') return void out.push(node)
    if (Array.isArray(node)) return node.forEach(walk)
    const children = (node as { children?: unknown[] }).children
    if (children) children.forEach(walk)
  }
  walk(screen.toJSON())
  return out
}

function expectInOrder(...texts: string[]) {
  const all = renderedTextOrder()
  const positions = texts.map((text) => all.findIndex((t) => t.includes(text)))
  positions.forEach((position, i) => expect({ text: texts[i], found: position >= 0 }).toEqual({ text: texts[i], found: true }))
  expect(positions).toEqual([...positions].sort((a, b) => a - b))
}

// Esta pantalla dibuja un árbol grande y el turno anterior necesita un pedido más después de la conversación:
// en Jest eso pasa del segundo que espera findBy por defecto. Se espera más; lo que se afirma es lo mismo.
const WAIT = { timeout: 5000 }

beforeEach(() => {
  jest.clearAllMocks()
  ;(useSession as jest.Mock).mockReturnValue(touristSession('u1', 'Ana Quispe'))
})

describe('historial del asistente', () => {
  it('al recargar la conversación cada respuesta conserva sus propias recomendaciones, en orden', async () => {
    backend({ messages: () => [...FIRST_TURN, ...SECOND_TURN], latest: () => CALMER })
    renderChat()

    expect(await screen.findByText('Rafting en el río Coroico', {}, WAIT)).toBeTruthy()
    expect(await screen.findByText('Museo de Etnografía y Folklore', {}, WAIT)).toBeTruthy()

    expectInOrder(
      'Quiero viajar por Bolivia y me gusta la aventura.',
      'Te armé una propuesta con aventura.',
      'Rafting en el río Coroico',
      'Menos aventura.',
      'Bajé la intensidad de la propuesta.',
      'Museo de Etnografía y Folklore',
    )
    // La vigente no se pide dos veces: ya vino con la conversación.
    expect(mockGet).not.toHaveBeenCalledWith('/api/ai/itineraries/it-2')
  })

  it('pedir "Menos aventura" agrega la respuesta nueva debajo sin borrar la anterior', async () => {
    let sent = false
    backend({
      messages: () => (sent ? [...FIRST_TURN, ...SECOND_TURN] : FIRST_TURN),
      latest: () => (sent ? CALMER : ADVENTURE),
    })
    mockPost.mockImplementation(() => {
      sent = true
      return ok({ assistantMessage: 'Bajé la intensidad de la propuesta.', clarificationNeeded: false, itinerary: CALMER, warnings: [], profileHints: [] })
    })
    renderChat()
    expect(await screen.findByText('Rafting en el río Coroico', {}, WAIT)).toBeTruthy()

    fireEvent.changeText(screen.getByLabelText('Mensaje para el asistente'), 'Menos aventura.')
    fireEvent.press(screen.getByLabelText('Enviar mensaje'))

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/ai/conversations/conv-1/messages', { content: 'Menos aventura.' }), WAIT)
    expect(await screen.findByText('Museo de Etnografía y Folklore', {}, WAIT)).toBeTruthy()

    // Lo del primer turno sigue ahí, arriba del pedido nuevo.
    expect(screen.getByText('Rafting en el río Coroico')).toBeTruthy()
    expectInOrder(
      'Te armé una propuesta con aventura.',
      'Rafting en el río Coroico',
      'Menos aventura.',
      'Bajé la intensidad de la propuesta.',
      'Museo de Etnografía y Folklore',
    )
  })

  it('las tarjetas de un turno anterior siguen siendo interactivas y actúan sobre su propia propuesta', async () => {
    backend({
      messages: () => [...FIRST_TURN, ...SECOND_TURN],
      latest: () => CALMER,
      overrides: {
        '/api/ai/itineraries/it-1/items/it-1-item/explanation': () =>
          ok({ explanation: 'Te propuse el rafting porque pediste aventura.', facts: ['Cupos disponibles: 8.'] }),
      },
    })
    mockPost.mockImplementation((url: string) =>
      url === '/api/ai/itineraries/it-1/save' ? ok({ ...ADVENTURE, status: 'SAVED' }) : Promise.reject(httpError(404, {})),
    )
    renderChat()
    await screen.findByText('Rafting en el río Coroico', {}, WAIT)

    // "¿Por qué?" del primer turno pide la explicación de SU ítem, no del vigente.
    fireEvent.press(screen.getByLabelText('¿Por qué Rafting en el río Coroico?'))
    expect(await screen.findByText('Te propuse el rafting porque pediste aventura.', {}, WAIT)).toBeTruthy()
    expect(mockGet).toHaveBeenCalledWith('/api/ai/itineraries/it-1/items/it-1-item/explanation')

    // Y "Guardar" de la primera tarjeta (la primera en pantalla) guarda la primera propuesta.
    fireEvent.press(screen.getAllByText('Guardar para después')[0])
    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/ai/itineraries/it-1/save'), WAIT)
    expect(mockPost).not.toHaveBeenCalledWith('/api/ai/itineraries/it-2/save')
  })

  it('al salir y volver a entrar a la conversación el historial completo sigue ahí', async () => {
    backend({ messages: () => [...FIRST_TURN, ...SECOND_TURN], latest: () => CALMER })
    const Wrapper = withClient(createTestQueryClient())
    const first = render(
      <Wrapper>
        <AssistantChatScreen />
      </Wrapper>,
    )
    await screen.findByText('Rafting en el río Coroico', {}, WAIT)
    await screen.findByText('Museo de Etnografía y Folklore', {}, WAIT)
    first.unmount()

    // Volver a la pantalla con la misma caché, como al navegar atrás y adelante: se ve todo de entrada.
    render(
      <Wrapper>
        <AssistantChatScreen />
      </Wrapper>,
    )
    expect(screen.getByText('Rafting en el río Coroico')).toBeTruthy()
    expect(screen.getByText('Museo de Etnografía y Folklore')).toBeTruthy()
    expectInOrder('Rafting en el río Coroico', 'Menos aventura.', 'Museo de Etnografía y Folklore')
  })

  it('tres turnos seguidos: cada propuesta queda bajo su respuesta', async () => {
    const third = proposal('it-3', 3, 'Bolivia con cultura', 'Recorrido por la Casa de la Moneda')
    const THIRD_TURN = [
      { id: 'm5', sender: 'TOURIST', content: 'Y algo cultural.' },
      { id: 'm6', sender: 'AI', content: 'Sumé más cultura.', itineraryId: 'it-3' },
    ]
    backend({
      messages: () => [...FIRST_TURN, ...SECOND_TURN, ...THIRD_TURN],
      latest: () => third,
      overrides: { '/api/ai/itineraries/it-3': () => ok(third) },
    })
    renderChat()

    expect(await screen.findByText('Recorrido por la Casa de la Moneda', {}, WAIT)).toBeTruthy()
    expect(await screen.findByText('Rafting en el río Coroico', {}, WAIT)).toBeTruthy()
    expect(await screen.findByText('Museo de Etnografía y Folklore', {}, WAIT)).toBeTruthy()
    expectInOrder(
      'Rafting en el río Coroico',
      'Menos aventura.',
      'Museo de Etnografía y Folklore',
      'Y algo cultural.',
      'Recorrido por la Casa de la Moneda',
    )
  })

  it('si una propuesta anterior no carga, sólo ese turno muestra el error', async () => {
    backend({
      messages: () => [...FIRST_TURN, ...SECOND_TURN],
      latest: () => CALMER,
      overrides: { '/api/ai/itineraries/it-1': () => Promise.reject(httpError(500, {})) },
    })
    renderChat()

    expect(await screen.findByText('No pudimos cargar esta propuesta.', {}, WAIT)).toBeTruthy()
    expect(screen.getByText('Museo de Etnografía y Folklore')).toBeTruthy()
    expect(screen.getByText('Quiero viajar por Bolivia y me gusta la aventura.')).toBeTruthy()
    expectInOrder('Te armé una propuesta con aventura.', 'No pudimos cargar esta propuesta.', 'Menos aventura.')
  })

  it('si el envío falla, lo que ya estaba en la conversación sigue a la vista', async () => {
    backend({ messages: () => FIRST_TURN, latest: () => ADVENTURE })
    mockPost.mockImplementation(() => Promise.reject(httpError(503, { detail: 'El asistente no está disponible.' })))
    renderChat()
    expect(await screen.findByText('Rafting en el río Coroico', {}, WAIT)).toBeTruthy()

    fireEvent.changeText(screen.getByLabelText('Mensaje para el asistente'), 'Menos aventura.')
    fireEvent.press(screen.getByLabelText('Enviar mensaje'))

    // Un 5xx nunca muestra el detalle del servidor (src/lib/errors.ts): se ve el mensaje genérico.
    expect(await screen.findByText('Tuvimos un problema de nuestro lado. Intentá de nuevo en un momento.', {}, WAIT)).toBeTruthy()
    expect(screen.getByText('Rafting en el río Coroico')).toBeTruthy()
    expect(screen.getByText('Te armé una propuesta con aventura.')).toBeTruthy()
  })

  it('con un backend anterior (sin itineraryId) muestra la propuesta vigente bajo la última respuesta, como antes', async () => {
    const legacy = [...FIRST_TURN, ...SECOND_TURN].map(({ itineraryId: _ignored, ...message }) => message)
    backend({ messages: () => legacy, latest: () => CALMER })
    renderChat()

    expect(await screen.findByText('Museo de Etnografía y Folklore', {}, WAIT)).toBeTruthy()
    expect(screen.queryByText('Rafting en el río Coroico')).toBeNull()
    expectInOrder('Bajé la intensidad de la propuesta.', 'Museo de Etnografía y Folklore')
  })
})
