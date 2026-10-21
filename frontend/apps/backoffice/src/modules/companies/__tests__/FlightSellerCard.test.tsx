import { providerFlightsApi } from '@turisclick/api-client'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { notAvailable, renderWithProviders } from '@/modules/provider-flights/__tests__/testUtils'
import { FlightSellerCard } from '../FlightSellerCard'

vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return { ...actual, providerFlightsApi: { ...actual.providerFlightsApi, setFlightSeller: vi.fn() } }
})
const setFlightSeller = vi.mocked(providerFlightsApi.setFlightSeller)

const COMPANY = { id: 'c1', name: 'Altura Viva', status: 'APPROVED', canSellFlights: false, flightSellerRegistration: null }

beforeEach(() => vi.clearAllMocks())

describe('FlightSellerCard', () => {
  it('autorizar exige el registro y después muestra el estado que devolvió el servidor', async () => {
    setFlightSeller.mockResolvedValue({ companyId: 'c1', canSellFlights: true, registration: 'DGAC 0123', authorizedAt: '2026-10-21T12:00:00Z' })
    renderWithProviders(<FlightSellerCard company={COMPANY} />)

    expect(screen.getByText('No autorizada')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Autorizar venta de vuelos' }))
    expect(screen.getByText(/hay que indicar el registro o la licencia/)).toBeTruthy()
    expect(setFlightSeller).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText('Registro o licencia'), { target: { value: ' DGAC 0123 ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar venta de vuelos' }))
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Autorizar' }))

    await waitFor(() => expect(setFlightSeller).toHaveBeenCalledTimes(1))
    expect(setFlightSeller.mock.calls[0].slice(1)).toEqual(['c1', { authorized: true, registration: 'DGAC 0123' }])
    expect(await screen.findByText('Autorizada')).toBeTruthy()
  })

  it('lee el estado cuando el backend ya lo expone y permite revocar', async () => {
    setFlightSeller.mockResolvedValue({ companyId: 'c1', canSellFlights: false, registration: 'DGAC 0123' })
    renderWithProviders(
      <FlightSellerCard
        company={{ ...COMPANY, canSellFlights: true, flightSellerRegistration: 'DGAC 0123', flightSellerAuthorizedAt: '2026-10-01T12:00:00Z' }}
      />,
    )

    expect(screen.getByText('Autorizada')).toBeTruthy()
    expect(screen.getByText(/con el registro DGAC 0123/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Autorizar venta de vuelos' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Revocar autorización' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/va a poder seguir registrando la emisión/)).toBeTruthy()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Revocar' }))

    await waitFor(() => expect(setFlightSeller.mock.calls[0].slice(1)).toEqual(['c1', { authorized: false, registration: 'DGAC 0123' }]))
    expect(await screen.findByText('No autorizada')).toBeTruthy()
  })

  it('si el servidor rechaza, el diálogo muestra su mensaje', async () => {
    setFlightSeller.mockRejectedValue(notAvailable())
    renderWithProviders(<FlightSellerCard company={COMPANY} />)

    fireEvent.change(screen.getByLabelText('Registro o licencia'), { target: { value: 'DGAC 0123' } })
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar venta de vuelos' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Autorizar' }))

    expect(await within(dialog).findByText('Los vuelos de operador todavía no están disponibles en este servidor.')).toBeTruthy()
  })
})
