import { render, screen, waitFor } from '@testing-library/react-native'
import FlightsTab from '@/../app/(tabs)/flights'
import FlightResultsRoute from '@/../app/flight-results'
import { useSession } from '@/auth/session'
import { createTestQueryClient, guestSession, ok, withClient } from '@/test-utils'

/**
 * Buscar vuelos no exige cuenta: ni la pestaña ni los resultados piden iniciar sesión. Si alguien vuelve a
 * envolverlas en RequireTourist, estos tests se rompen. La sesión se pide al empezar una reserva.
 */

const mockGet = jest.fn()
const mockPost = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) },
}))
jest.mock('expo-router', () => ({
  useRouter: () => ({ push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }),
  useLocalSearchParams: () => ({ o: 'MAD', d: 'VVI', dep: '2026-11-02', ad: '1', cab: 'ECONOMY' }),
  Stack: { Screen: () => null },
}))
jest.mock('@/auth/session', () => ({ useSession: jest.fn() }))

const mockedUseSession = useSession as jest.MockedFunction<typeof useSession>

beforeEach(() => {
  jest.clearAllMocks()
  mockedUseSession.mockReturnValue(guestSession())
})

describe('vuelos sin sesión', () => {
  it('la pestaña Vuelos muestra el buscador sin pedir que inicies sesión', () => {
    render(<FlightsTab />, { wrapper: withClient(createTestQueryClient()) })

    expect(screen.getByLabelText('Buscar vuelos')).toBeTruthy()
    expect(screen.queryByText(/iniciá sesión/i)).toBeNull()
  })

  it('los resultados se consultan y se muestran sin sesión, rotulados DEMO', async () => {
    mockPost.mockReturnValue(
      ok({
        originIata: 'MAD',
        originLabel: 'Madrid (MAD)',
        destinationIata: 'VVI',
        destinationLabel: 'Santa Cruz de la Sierra (VVI)',
        departureDate: '2026-11-02',
        passengers: 1,
        adults: 1,
        childrenAges: [],
        infants: 0,
        cabinClass: 'ECONOMY',
        inventoryMode: 'SIMULATED',
        testMode: true,
        bookable: false,
        options: [],
        packages: [],
        notice: 'No encontramos vuelos para el 02/11.',
      }),
    )

    render(<FlightResultsRoute />, { wrapper: withClient(createTestQueryClient()) })

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/flights/search', expect.objectContaining({ originIata: 'MAD' })))
    expect(await screen.findByTestId('inventory-banner')).toBeTruthy()
    expect(screen.queryByText(/iniciá sesión/i)).toBeNull()
  })
})
