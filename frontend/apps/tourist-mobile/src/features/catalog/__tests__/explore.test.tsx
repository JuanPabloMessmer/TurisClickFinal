import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import ExploreScreen from '@/../app/(tabs)/explore'
import HomeScreen from '@/../app/(tabs)/index'
import { useSession } from '@/auth/session'
import { catalogKeys } from '@/features/catalog/queries'
import { createTestQueryClient, guestSession, ok, touristSession, withClient } from '@/test-utils'

/**
 * Explorar a nivel de pantalla: qué llega realmente al backend y qué pasa con los filtros cuando cambia
 * la sesión. La pantalla vive montada en su tab, así que su estado sobrevive a logout/login si nadie lo
 * resetea — ese fue el bug encontrado en la QA física.
 */

const mockGet = jest.fn()
jest.mock('@/lib/httpClient', () => ({ httpClient: { get: (...args: unknown[]) => mockGet(...args) } }))
/** Params de la ruta de Explorar; los tests los cambian para simular un toque nuevo desde Inicio. */
let mockParams: Record<string, string | undefined> = {}
const mockPush = jest.fn()
jest.mock('expo-router', () => ({
  useLocalSearchParams: () => mockParams,
  useRouter: () => ({ push: mockPush, back: jest.fn() }),
  Link: ({ children }: { children: React.ReactNode }) => children,
}))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

function catalogBackend(url: string) {
  switch (url) {
    case '/api/destinations':
      return ok([
        { id: 'city-lpz', name: 'La Paz' },
        { id: 'city-scz', name: 'Santa Cruz' },
      ])
    case '/api/categories':
      return ok([
        { id: 'cat-nat', name: 'Naturaleza' },
        { id: 'cat-gas', name: 'Gastronomía' },
      ])
    default:
      return ok({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 })
  }
}

/** Los query params de la última búsqueda enviada a `url`. */
function lastSearch(url: '/api/experiences' | '/api/packages') {
  const calls = mockGet.mock.calls.filter(([calledUrl]) => calledUrl === url)
  return (calls.at(-1)?.[1] as { params?: Record<string, unknown> } | undefined)?.params
}

let client = createTestQueryClient()
let Wrapper = withClient(client)
const app = () => (
  <Wrapper>
    <ExploreScreen />
  </Wrapper>
)

async function applyFilters(labels: string[]) {
  fireEvent.press(screen.getByLabelText(/^Filtros/))
  for (const label of labels) {
    await waitFor(() => expect(screen.getByText(label)).toBeTruthy())
    fireEvent.press(screen.getByText(label))
  }
  fireEvent.press(screen.getByText('Ver resultados'))
}

beforeEach(() => {
  jest.clearAllMocks()
  mockParams = {}
  client = createTestQueryClient()
  Wrapper = withClient(client)
  mockGet.mockImplementation(catalogBackend)
})

afterEach(() => client.clear())

describe('filtros y cambios de sesión', () => {
  it('Tourist A aplica filtros → logout → Tourist B: Explorar arranca sin los filtros de A', async () => {
    mockedUseSession.mockReturnValue(touristSession('user-a'))
    const { rerender } = render(app())

    fireEvent.press(screen.getByText('Paquetes'))
    await applyFilters(['La Paz', 'Naturaleza', 'Hasta 300'])
    await waitFor(() => expect(screen.getByLabelText('Filtros, 3 aplicados')).toBeTruthy())
    expect(lastSearch('/api/packages')).toMatchObject({ destinationId: 'city-lpz', categoryId: 'cat-nat', priceMax: 300 })

    // Logout
    mockedUseSession.mockReturnValue(guestSession())
    rerender(app())
    await waitFor(() => expect(screen.getByLabelText('Filtros')).toBeTruthy())

    // Tourist B
    mockedUseSession.mockReturnValue(touristSession('user-b', 'Bruno Mamani'))
    rerender(app())

    await waitFor(() => expect(screen.getByLabelText('Filtros')).toBeTruthy())
    expect(screen.queryByLabelText(/aplicados/)).toBeNull()
    expect(screen.getAllByRole('tab')[0].props.accessibilityState).toMatchObject({ selected: true }) // Experiencias
    await waitFor(() =>
      expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: undefined, categoryId: undefined, priceMax: undefined }),
    )

    // El reset es solo del estado de Explorar: la caché pública del catálogo sigue ahí.
    expect(client.getQueryData(catalogKeys.categories)).toBeDefined()
    expect(client.getQueryData(catalogKeys.cities)).toBeDefined()
  })

  it('mientras la sesión no cambia, los filtros se conservan (ej. ir a un detalle y volver)', async () => {
    mockedUseSession.mockReturnValue(touristSession('user-a'))
    const { rerender } = render(app())
    await applyFilters(['Santa Cruz', 'Gastronomía'])
    await waitFor(() => expect(screen.getByLabelText('Filtros, 2 aplicados')).toBeTruthy())

    // La tab queda montada al abrir un detalle; al volver solo hay re-renders con la misma sesión.
    mockedUseSession.mockReturnValue(touristSession('user-a'))
    rerender(app())
    rerender(app())

    expect(screen.getByLabelText('Filtros, 2 aplicados')).toBeTruthy()
    // El badge refleja el estado de inmediato; la búsqueda la dispara TanStack después: se espera.
    await waitFor(() =>
      expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: 'city-scz', categoryId: 'cat-gas' }),
    )
  })

  it('un invitado que filtra y después inicia sesión conserva sus filtros (no hay otra persona involucrada)', async () => {
    mockedUseSession.mockReturnValue(guestSession())
    const { rerender } = render(app())
    await applyFilters(['La Paz'])
    await waitFor(() => expect(screen.getByLabelText('Filtros, 1 aplicados')).toBeTruthy())

    mockedUseSession.mockReturnValue(touristSession('user-a'))
    rerender(app())

    await waitFor(() => expect(screen.getByLabelText('Filtros, 1 aplicados')).toBeTruthy())
  })
})

describe('combinación de filtros (contrato real del backend)', () => {
  beforeEach(() => mockedUseSession.mockReturnValue(guestSession()))

  it('experiencias: filtros de dimensiones distintas viajan juntos en una sola búsqueda', async () => {
    render(app())

    await applyFilters(['La Paz', 'Naturaleza', 'Hasta 300', 'Con cupo desde hoy'])

    await waitFor(() => expect(screen.getByLabelText('Filtros, 4 aplicados')).toBeTruthy())
    expect(lastSearch('/api/experiences')).toMatchObject({
      destinationId: 'city-lpz',
      categoryId: 'cat-nat',
      priceMax: 300,
      availableFrom: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
    })
  })

  it('paquetes: destino + categoría + precio + duración viajan juntos', async () => {
    render(app())
    fireEvent.press(screen.getByText('Paquetes'))

    await applyFilters(['Santa Cruz', 'Gastronomía', 'Hasta 600', '4 a 7 días'])

    await waitFor(() => expect(screen.getByLabelText('Filtros, 4 aplicados')).toBeTruthy())
    expect(lastSearch('/api/packages')).toMatchObject({
      destinationId: 'city-scz',
      categoryId: 'cat-gas',
      priceMax: 600,
      durationDaysMin: 4,
      durationDaysMax: 7,
    })
  })

  it('dentro de una MISMA dimensión la selección es única: elegir otro destino reemplaza al anterior', async () => {
    render(app())
    await applyFilters(['La Paz'])
    await waitFor(() => expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: 'city-lpz' }))

    await applyFilters(['Santa Cruz'])

    // El endpoint recibe un único `destinationId` (Guid?): no hay "La Paz + Santa Cruz" en el contrato.
    await waitFor(() => expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: 'city-scz' }))
    expect(screen.getByLabelText('Filtros, 1 aplicados')).toBeTruthy()
  })
})

/**
 * Hallazgo de la QA física: Explorar solo leía el destino del acceso rápido al montarse. Como la tab queda
 * montada, un segundo toque desde Inicio con otro destino no tenía efecto.
 */
describe('acceso rápido a destinos desde Inicio', () => {
  beforeEach(() => mockedUseSession.mockReturnValue(guestSession()))

  it('Inicio envía el destino con una marca única por toque', async () => {
    const Home = () => (
      <Wrapper>
        <HomeScreen />
      </Wrapper>
    )
    render(<Home />)
    await waitFor(() => expect(screen.getByLabelText('Explorar La Paz')).toBeTruthy())

    fireEvent.press(screen.getByLabelText('Explorar La Paz'))
    fireEvent.press(screen.getByLabelText('Explorar La Paz'))

    expect(mockPush).toHaveBeenCalledTimes(2)
    const [first, second] = mockPush.mock.calls.map(([href]) => href as { pathname: string; params: Record<string, string> })
    expect(first).toMatchObject({ pathname: '/explore', params: { destinationId: 'city-lpz' } })
    expect(first.params.shortcutAt).toEqual(expect.any(String))
    expect(second.params.shortcutAt).not.toBe(first.params.shortcutAt)
  })

  it('destino A y luego destino B con Explorar ya montada: reemplaza por B, sin acumular', async () => {
    mockParams = { destinationId: 'city-lpz', shortcutAt: 't1' }
    const { rerender } = render(app())
    await waitFor(() => expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: 'city-lpz' }))
    expect(screen.getByLabelText('Filtros, 1 aplicados')).toBeTruthy()

    mockParams = { destinationId: 'city-scz', shortcutAt: 't2' }
    rerender(app())

    await waitFor(() => expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: 'city-scz' }))
    expect(screen.getByLabelText('Filtros, 1 aplicados')).toBeTruthy()
    expect(typeof lastSearch('/api/experiences')?.destinationId).toBe('string')
  })

  it('el acceso rápido es una búsqueda nueva: descarta los demás filtros para no mezclar búsquedas', async () => {
    mockParams = { destinationId: 'city-lpz', shortcutAt: 't1' }
    const { rerender } = render(app())
    await applyFilters(['Naturaleza', 'Hasta 300', 'Con cupo desde hoy'])
    await waitFor(() => expect(screen.getByLabelText('Filtros, 4 aplicados')).toBeTruthy())

    mockParams = { destinationId: 'city-scz', shortcutAt: 't2' }
    rerender(app())

    await waitFor(() => expect(screen.getByLabelText('Filtros, 1 aplicados')).toBeTruthy())
    await waitFor(() =>
      expect(lastSearch('/api/experiences')).toMatchObject({
        destinationId: 'city-scz',
        categoryId: undefined,
        priceMax: undefined,
        availableFrom: undefined,
      }),
    )
  })

  it('tocar OTRA VEZ el mismo destino lo reaplica aunque se hubiera cambiado desde el panel', async () => {
    mockParams = { destinationId: 'city-lpz', shortcutAt: 't1' }
    const { rerender } = render(app())
    await applyFilters(['Santa Cruz'])
    await waitFor(() => expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: 'city-scz' }))

    mockParams = { destinationId: 'city-lpz', shortcutAt: 't2' }
    rerender(app())

    await waitFor(() => expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: 'city-lpz' }))
  })

  it('sin un toque nuevo, los re-renders conservan lo que se eligió dentro de Explorar', async () => {
    mockParams = { destinationId: 'city-lpz', shortcutAt: 't1' }
    const { rerender } = render(app())
    await applyFilters(['Naturaleza'])
    await waitFor(() => expect(screen.getByLabelText('Filtros, 2 aplicados')).toBeTruthy())

    // Mismos params (ej. volver de un detalle): no es un toque nuevo.
    rerender(app())
    rerender(app())

    expect(screen.getByLabelText('Filtros, 2 aplicados')).toBeTruthy()
    await waitFor(() => expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: 'city-lpz', categoryId: 'cat-nat' }))
  })

  it('el reset por cambio de usuario sigue funcionando y no reaplica un destino viejo', async () => {
    mockedUseSession.mockReturnValue(touristSession('user-a'))
    mockParams = { destinationId: 'city-lpz', shortcutAt: 't1' }
    const { rerender } = render(app())
    await waitFor(() => expect(screen.getByLabelText('Filtros, 1 aplicados')).toBeTruthy())

    // Logout: los params de la tab siguen teniendo el acceso rápido viejo, pero no es un toque nuevo.
    mockedUseSession.mockReturnValue(guestSession())
    rerender(app())
    await waitFor(() => expect(screen.getByLabelText('Filtros')).toBeTruthy())

    mockedUseSession.mockReturnValue(touristSession('user-b', 'Bruno Mamani'))
    rerender(app())

    await waitFor(() => expect(screen.getByLabelText('Filtros')).toBeTruthy())
    await waitFor(() => expect(lastSearch('/api/experiences')).toMatchObject({ destinationId: undefined }))
    expect(client.getQueryData(catalogKeys.cities)).toBeDefined()
  })
})
