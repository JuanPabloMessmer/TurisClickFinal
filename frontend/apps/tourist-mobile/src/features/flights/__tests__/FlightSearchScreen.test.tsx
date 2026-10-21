import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'
import { FlightSearchScreen } from '@/features/flights/FlightSearchScreen'
import { createTestQueryClient, ok, withClient } from '@/test-utils'

/**
 * El buscador de vuelos (la pestaña "Vuelos").
 *
 * Se protege lo que hace que se use como un buscador conocido: autocompletado de aeropuertos del mundo
 * como origen y sólo de Bolivia como destino, calendario de ida y vuelta, pasajeros con sus reglas, y que
 * buscar abra los resultados con exactamente lo elegido —nunca un precio ni un id de oferta—.
 */

const mockGet = jest.fn()
jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args), post: jest.fn() },
}))

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), canGoBack: jest.fn(() => true) }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))

const TODAY = '2026-10-19'

const AIRPORTS = {
  MAD: { iata: 'MAD', name: 'Adolfo Suárez Madrid–Barajas Airport', city: 'Madrid', country: 'España', countryCode: 'ES', label: 'Madrid (MAD)' },
  VVI: { iata: 'VVI', name: 'Viru Viru', city: 'Santa Cruz de la Sierra', country: 'Bolivia', countryCode: 'BO', label: 'Santa Cruz de la Sierra (VVI)' },
  LPB: { iata: 'LPB', name: 'El Alto', city: 'La Paz', country: 'Bolivia', countryCode: 'BO', label: 'La Paz (LPB)' },
  CIJ: { iata: 'CIJ', name: 'Capitán Aníbal Arab Airport', city: 'Cobija', country: 'Bolivia', countryCode: 'BO', label: 'Cobija (CIJ)' },
}

/** Responde como el backend: el destino sólo trae Bolivia; el origen, lo que coincida con el texto. */
function airportApi(url: string, config?: { params?: { q?: string; role?: string } }) {
  if (url !== '/api/airports/search') return Promise.reject(new Error(`GET inesperado: ${url}`))
  const q = (config?.params?.q ?? '').toLowerCase()
  const role = config?.params?.role
  const all = Object.values(AIRPORTS).filter((a) => role !== 'destination' || a.countryCode === 'BO')
  return ok(q ? all.filter((a) => `${a.city} ${a.name} ${a.iata} ${a.country}`.toLowerCase().includes(q)) : all)
}

function renderScreen() {
  render(<FlightSearchScreen today={TODAY} />, { wrapper: withClient(createTestQueryClient()) })
}

async function choose(field: 'campo-origen' | 'campo-destino', text: string, airportLabel: RegExp) {
  fireEvent.press(screen.getByTestId(field))
  fireEvent.changeText(
    screen.getByLabelText(field === 'campo-origen' ? 'Buscar aeropuerto de origen' : 'Buscar aeropuerto de destino'),
    text,
  )
  const row = await screen.findByLabelText(airportLabel, {}, { timeout: 3000 })
  fireEvent.press(row)
}

beforeEach(() => {
  jest.clearAllMocks()
  mockGet.mockImplementation(airportApi)
})

describe('buscador de vuelos', () => {
  it('el origen se busca en todo el mundo, por ciudad, y cada fila muestra ciudad, país, aeropuerto y código', async () => {
    renderScreen()

    fireEvent.press(screen.getByTestId('campo-origen'))
    fireEvent.changeText(screen.getByLabelText('Buscar aeropuerto de origen'), 'madrid')

    const row = await screen.findByLabelText('Madrid (MAD), Adolfo Suárez Madrid–Barajas Airport, España', {}, { timeout: 3000 })
    expect(screen.getByText('Madrid, España')).toBeTruthy()
    expect(screen.getByText('MAD')).toBeTruthy()

    await waitFor(() =>
      expect(mockGet).toHaveBeenCalledWith(
        '/api/airports/search',
        expect.objectContaining({ params: expect.objectContaining({ q: 'madrid', role: 'origin' }) }),
      ),
    )

    fireEvent.press(row)
    expect(screen.getByLabelText('Desde: Madrid (MAD)')).toBeTruthy()
  })

  it('el destino sólo ofrece Bolivia, incluidos los aeropuertos chicos, y explica por qué', async () => {
    renderScreen()

    fireEvent.press(screen.getByTestId('campo-destino'))

    expect(await screen.findByLabelText(/Cobija \(CIJ\)/, {}, { timeout: 3000 })).toBeTruthy()
    expect(screen.getByText('Por ahora, los destinos son aeropuertos de Bolivia.')).toBeTruthy()

    fireEvent.changeText(screen.getByLabelText('Buscar aeropuerto de destino'), 'madrid')
    expect(await screen.findByText('Por ahora sólo se puede volar a destinos en Bolivia.', {}, { timeout: 3000 })).toBeTruthy()
  })

  it('no deja buscar sin origen y destino, ni con el mismo aeropuerto de los dos lados', async () => {
    renderScreen()

    expect(screen.getByLabelText('Buscar vuelos')).toBeDisabled()

    await choose('campo-origen', 'la paz', /^La Paz \(LPB\)/)
    expect(screen.getByLabelText('Buscar vuelos')).toBeDisabled()
    expect(screen.getByText('Elegí a dónde vas.')).toBeTruthy()

    // El destino no ofrece el aeropuerto ya elegido como origen.
    fireEvent.press(screen.getByTestId('campo-destino'))
    await screen.findByLabelText(/Cobija \(CIJ\)/, {}, { timeout: 3000 })
    expect(screen.queryByLabelText(/^La Paz \(LPB\)/)).toBeNull()
  })

  it('busca con lo elegido y abre los resultados — sin precio ni oferta en la búsqueda', async () => {
    renderScreen()

    await choose('campo-origen', 'madrid', /^Madrid \(MAD\)/)
    await choose('campo-destino', 'santa', /^Santa Cruz de la Sierra \(VVI\)/)
    fireEvent.press(screen.getByLabelText('Buscar vuelos'))

    expect(mockRouter.push).toHaveBeenCalledWith({
      pathname: '/flight-results',
      params: { o: 'MAD', d: 'VVI', dep: '2026-11-02', ret: '2026-11-09', ad: '1', cab: 'ECONOMY' },
    })
    const params = mockRouter.push.mock.calls[0][0].params
    expect(params).not.toHaveProperty('price')
    expect(params).not.toHaveProperty('offerId')
  })

  it('las fechas se eligen en un calendario: la ida, después la vuelta, y nunca una vuelta antes de la ida', async () => {
    renderScreen()

    fireEvent.press(screen.getByTestId('campo-fechas'))
    // Abre en el mes de la ida propuesta (noviembre); se vuelve a octubre, el mes de hoy.
    expect(screen.getByText(/noviembre/i)).toBeTruthy()
    fireEvent.press(screen.getByLabelText('Mes anterior'))
    expect(screen.getByLabelText('Mes anterior')).toBeDisabled()
    // Los días pasados no se pueden tocar.
    expect(screen.getByLabelText(/18 de octubre, no disponible/)).toBeDisabled()

    fireEvent.press(screen.getByLabelText(/^martes, 27 de octubre/))
    // Elegida la ida, pasa solo a la vuelta: los días anteriores a la ida quedan deshabilitados.
    expect(screen.getByLabelText(/^lunes, 26 de octubre, no disponible/)).toBeDisabled()
    expect(screen.getByLabelText(/^Vuelta:/)).toBeSelected()

    fireEvent.press(screen.getByLabelText(/^viernes, 30 de octubre/))
    fireEvent.press(screen.getByLabelText('Listo'))

    await choose('campo-origen', 'madrid', /^Madrid \(MAD\)/)
    await choose('campo-destino', 'cobija', /^Cobija \(CIJ\)/)
    fireEvent.press(screen.getByLabelText('Buscar vuelos'))

    expect(mockRouter.push).toHaveBeenCalledWith(
      expect.objectContaining({ params: expect.objectContaining({ dep: '2026-10-27', ret: '2026-10-30', d: 'CIJ' }) }),
    )
  })

  it('sólo ida no manda fecha de vuelta', async () => {
    renderScreen()

    fireEvent.press(screen.getByRole('tab', { name: 'Sólo ida' }))
    await choose('campo-origen', 'madrid', /^Madrid \(MAD\)/)
    await choose('campo-destino', 'santa', /^Santa Cruz de la Sierra \(VVI\)/)
    fireEvent.press(screen.getByLabelText('Buscar vuelos'))

    expect(mockRouter.push.mock.calls[0][0].params).not.toHaveProperty('ret')
  })

  it('pasajeros: niños con su edad, infantes sólo con un adulto cada uno, y cabina', async () => {
    renderScreen()

    fireEvent.press(screen.getByTestId('campo-pasajeros'))
    // Con un adulto, un infante como mucho.
    fireEvent.press(screen.getByLabelText('Sumar infante'))
    expect(screen.getByLabelText('Sumar infante')).toBeDisabled()
    expect(screen.getByText('Cada infante viaja en brazos de un adulto.')).toBeTruthy()
    // Y ese adulto ya no se puede quitar: dejaría al infante sin brazos.
    expect(screen.getByLabelText('Quitar adulto')).toBeDisabled()

    fireEvent.press(screen.getByLabelText('Sumar adulto'))
    fireEvent.press(screen.getByLabelText('Sumar niño'))
    // Un niño sin edad no deja confirmar: el proveedor la necesita para tarifarlo.
    expect(screen.getByLabelText('Elegí la edad de cada niño')).toBeDisabled()
    fireEvent.press(screen.getByLabelText('7 años'))
    fireEvent.press(screen.getByLabelText('Business'))
    fireEvent.press(screen.getByLabelText('Listo'))

    expect(screen.getByLabelText('Pasajeros y cabina: 2 adultos, 1 niño, 1 infante · Business')).toBeTruthy()

    await choose('campo-origen', 'madrid', /^Madrid \(MAD\)/)
    await choose('campo-destino', 'santa', /^Santa Cruz de la Sierra \(VVI\)/)
    fireEvent.press(screen.getByLabelText('Buscar vuelos'))

    expect(mockRouter.push.mock.calls[0][0].params).toMatchObject({ ad: '2', ch: '7', inf: '1', cab: 'BUSINESS' })
  })

  it('no se pasa de 9 pasajeros', () => {
    renderScreen()

    fireEvent.press(screen.getByTestId('campo-pasajeros'))
    for (let i = 0; i < 8; i++) fireEvent.press(screen.getByLabelText('Sumar adulto'))

    expect(screen.getByLabelText('Adultos: 9')).toBeTruthy()
    expect(screen.getByLabelText('Sumar adulto')).toBeDisabled()
    expect(screen.getByLabelText('Sumar niño')).toBeDisabled()
    expect(screen.getByText(/hasta 9 pasajeros/)).toBeTruthy()
  })

  it('invertir el viaje sólo se ofrece cuando el origen también puede ser destino', async () => {
    renderScreen()

    await choose('campo-origen', 'madrid', /^Madrid \(MAD\)/)
    await choose('campo-destino', 'santa', /^Santa Cruz de la Sierra \(VVI\)/)
    expect(screen.getByLabelText('Invertir origen y destino')).toBeDisabled()
  })
})
