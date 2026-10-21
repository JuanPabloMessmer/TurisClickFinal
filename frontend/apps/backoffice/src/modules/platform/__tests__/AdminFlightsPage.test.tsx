import { providerFlightsApi } from '@turisclick/api-client'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { notAvailable, renderWithProviders } from '@/modules/provider-flights/__tests__/testUtils'
import { AdminFlightsPage } from '../AdminFlightsPage'

vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    providerFlightsApi: { ...actual.providerFlightsApi, adminListFlights: vi.fn(), suspendFlight: vi.fn(), unsuspendFlight: vi.fn() },
  }
})
const adminListFlights = vi.mocked(providerFlightsApi.adminListFlights)
const suspendFlight = vi.mocked(providerFlightsApi.suspendFlight)
const unsuspendFlight = vi.mocked(providerFlightsApi.unsuspendFlight)

const FLIGHT = {
  id: 'f1',
  companyName: 'Altura Viva',
  carrierName: 'Boliviana de Aviación',
  carrierIata: 'OB',
  flightNumber: '712',
  originIata: 'MIA',
  destinationIata: 'VVI',
  status: 'PUBLISHED',
  suspended: false,
  upcomingDepartures: 3,
}

beforeEach(() => vi.clearAllMocks())

describe('AdminFlightsPage', () => {
  it('suspende un vuelo después de explicar que lo vendido sigue', async () => {
    adminListFlights.mockResolvedValue({ items: [FLIGHT], page: 1, pageSize: 20, totalCount: 1 })
    suspendFlight.mockResolvedValue({ ...FLIGHT, suspended: true })
    renderWithProviders(<AdminFlightsPage />)

    fireEvent.click(await screen.findByRole('button', { name: /Suspender Boliviana de Aviación OB 712/ }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Las reservas que ya existen no se cancelan/)).toBeTruthy()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Suspender' }))

    await waitFor(() => expect(suspendFlight).toHaveBeenCalledTimes(1))
    expect(suspendFlight.mock.calls[0][1]).toBe('f1')
  })

  it('reactiva un vuelo suspendido', async () => {
    adminListFlights.mockResolvedValue({ items: [{ ...FLIGHT, suspended: true }], page: 1, pageSize: 20, totalCount: 1 })
    unsuspendFlight.mockResolvedValue(FLIGHT)
    renderWithProviders(<AdminFlightsPage />)

    expect(await screen.findByText('Suspendido')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Reactivar Boliviana/ }))
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Reactivar' }))

    await waitFor(() => expect(unsuspendFlight).toHaveBeenCalledTimes(1))
  })

  it('con 501 muestra el estado "todavía no disponible"', async () => {
    adminListFlights.mockRejectedValue(notAvailable())
    renderWithProviders(<AdminFlightsPage />)

    expect(await screen.findByText('Vuelos de operador: todavía no disponible')).toBeTruthy()
  })
})
