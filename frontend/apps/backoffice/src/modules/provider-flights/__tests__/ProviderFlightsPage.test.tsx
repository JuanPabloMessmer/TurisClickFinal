import { providerFlightsApi } from '@turisclick/api-client'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ProviderFlightsPage } from '../ProviderFlightsPage'
import { notAuthorized, notAvailable, renderWithProviders } from './testUtils'

/**
 * Vuelos del operador mientras el backend todavía puede responder 501: la pantalla tiene que explicar el estado
 * (no disponible / empresa no autorizada) en vez de mostrar un error genérico, y rotular todo como DEMO.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    providerFlightsApi: { ...actual.providerFlightsApi, listMyFlights: vi.fn(), publishFlight: vi.fn(), archiveFlight: vi.fn() },
  }
})
const listMyFlights = vi.mocked(providerFlightsApi.listMyFlights)
const publishFlight = vi.mocked(providerFlightsApi.publishFlight)

const DRAFT = {
  id: 'f1',
  carrierName: 'Boliviana de Aviación',
  carrierIata: 'OB',
  flightNumber: '712',
  originIata: 'MIA',
  originLabel: 'Miami',
  destinationIata: 'VVI',
  destinationLabel: 'Santa Cruz (Viru Viru)',
  cabinClass: 'ECONOMY',
  status: 'DRAFT',
  suspended: false,
  upcomingDepartures: 2,
}

beforeEach(() => vi.clearAllMocks())

describe('ProviderFlightsPage', () => {
  it('lista los vuelos con ruta, estado y el rótulo DEMO', async () => {
    listMyFlights.mockResolvedValue([DRAFT, { ...DRAFT, id: 'f2', status: 'PUBLISHED', suspended: true }])
    renderWithProviders(<ProviderFlightsPage />)

    expect(await screen.findAllByText('MIA → VVI')).toHaveLength(2)
    expect(screen.getByText('DEMO')).toBeTruthy()
    expect(screen.getByText('Borrador')).toBeTruthy()
    expect(screen.getByText('Suspendido por TurisClick')).toBeTruthy()
  })

  it('con 501 muestra "todavía no disponible" con el mensaje del servidor, sin botón de alta', async () => {
    listMyFlights.mockRejectedValue(notAvailable())
    renderWithProviders(<ProviderFlightsPage />)

    expect(await screen.findByText('Vuelos de operador: todavía no disponible')).toBeTruthy()
    expect(screen.getByText('Los vuelos de operador todavía no están disponibles en este servidor.')).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Nuevo vuelo/ })).toBeNull()
  })

  it('con 403 explica que la empresa no está autorizada para vender vuelos', async () => {
    listMyFlights.mockRejectedValue(notAuthorized())
    renderWithProviders(<ProviderFlightsPage />)

    expect(await screen.findByText('Tu empresa no está autorizada para vender vuelos')).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Nuevo vuelo/ })).toBeNull()
  })

  it('publicar con doble clic llama una sola vez y muestra el error del servidor', async () => {
    listMyFlights.mockResolvedValue([DRAFT])
    publishFlight.mockRejectedValue(notAvailable())
    renderWithProviders(<ProviderFlightsPage />)

    const publish = await screen.findByRole('button', { name: /Publicar/ })
    fireEvent.click(publish)
    fireEvent.click(publish)

    await waitFor(() => expect(screen.getByText('Los vuelos de operador todavía no están disponibles en este servidor.')).toBeTruthy())
    expect(publishFlight).toHaveBeenCalledTimes(1)
  })
})
