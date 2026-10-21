import { providerFlightsApi, type ReservationItemResponse } from '@turisclick/api-client'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { notAvailable, renderWithProviders } from '@/modules/provider-flights/__tests__/testUtils'
import { FlightIssuanceSection } from '../FlightIssuanceSection'

/**
 * Emisión de un vuelo de operador. La regla de producto: nada dice "emitido" hasta que el operador lo registra,
 * y se carga un número de pasaje por pasajero, infantes incluidos, en el orden de la reserva.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    providerFlightsApi: { ...actual.providerFlightsApi, recordIssuance: vi.fn(), recordIssuanceFailure: vi.fn() },
  }
})
const recordIssuance = vi.mocked(providerFlightsApi.recordIssuance)
const recordIssuanceFailure = vi.mocked(providerFlightsApi.recordIssuanceFailure)

function flightItem(itemStatus: string, issuanceStatus: string, extra: Record<string, unknown> = {}): ReservationItemResponse {
  return {
    id: 'item-1',
    status: itemStatus,
    productType: 'FLIGHT',
    flight: {
      originIata: 'MIA',
      destinationIata: 'VVI',
      carrierName: 'Boliviana de Aviación',
      carrierIata: 'OB',
      flightNumber: '712',
      departureAt: '2026-11-10T08:30:00',
      arrivalAt: '2026-11-10T14:05:00',
      cabinClass: 'ECONOMY',
      checkedBags: 1,
      carryOnBags: 1,
      issuanceStatus,
      passengers: [
        { type: 'ADULT', givenName: 'Ana', familyName: 'Prueba', bornOn: '1990-01-01' },
        { type: 'INFANT', givenName: 'Leo', familyName: 'Prueba', bornOn: '2026-01-01' },
      ],
      demo: true,
      ...extra,
    },
  }
}

beforeEach(() => vi.clearAllMocks())

describe('FlightIssuanceSection', () => {
  it('antes de pagar no hay nada que emitir y nada dice "emitido"', () => {
    renderWithProviders(<FlightIssuanceSection item={flightItem('PENDING_PAYMENT', 'NOT_REQUIRED')} />)

    expect(screen.getByText(/el pasaje se emite cuando el viajero paga/)).toBeTruthy()
    expect(screen.queryByText(/^Emitido$/)).toBeNull()
    expect(screen.queryByText(/Pasaje emitido/i)).toBeNull()
    expect(screen.queryByRole('button', { name: /Registrar emisión/ })).toBeNull()
    expect(screen.getByText('DEMO')).toBeTruthy()
  })

  it('pagado y pendiente: pide la emisión, sin decir "emitido"', () => {
    renderWithProviders(<FlightIssuanceSection item={flightItem('CONFIRMED', 'PENDING')} />)

    expect(screen.getByText('Emisión pendiente')).toBeTruthy()
    expect(screen.queryByText(/^Emitido$/)).toBeNull()
    expect(screen.getByRole('button', { name: /Registrar emisión/ })).toBeTruthy()
  })

  it('registra un pasaje por pasajero, infantes incluidos y en orden', async () => {
    recordIssuance.mockResolvedValue({ reservationItemId: 'item-1', issuanceStatus: 'ISSUED' })
    renderWithProviders(<FlightIssuanceSection item={flightItem('CONFIRMED', 'PENDING')} />)

    fireEvent.click(screen.getByRole('button', { name: /Registrar emisión/ }))
    const dialog = await screen.findByRole('dialog')

    // Falta el del infante: no se envía.
    fireEvent.change(within(dialog).getByLabelText(/Pasaje de Ana Prueba/), { target: { value: '9301234567890' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar emisión' }))
    expect(await within(dialog).findByText('Cargá el número de pasaje de cada pasajero.')).toBeTruthy()
    expect(recordIssuance).not.toHaveBeenCalled()

    fireEvent.change(within(dialog).getByLabelText(/Pasaje de Leo Prueba \(Infante\)/), { target: { value: ' 9301234567891 ' } })
    fireEvent.change(within(dialog).getByLabelText(/Localizador/), { target: { value: 'abc123' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar emisión' }))

    await waitFor(() => expect(recordIssuance).toHaveBeenCalledTimes(1))
    expect(recordIssuance.mock.calls[0].slice(1)).toEqual([
      'item-1',
      { ticketNumbers: ['9301234567890', '9301234567891'], bookingReference: 'ABC123', note: null },
    ])
  })

  it('valida el largo de cada número de pasaje', async () => {
    renderWithProviders(<FlightIssuanceSection item={flightItem('CONFIRMED', 'PENDING')} />)
    fireEvent.click(screen.getByRole('button', { name: /Registrar emisión/ }))
    const dialog = await screen.findByRole('dialog')

    fireEvent.change(within(dialog).getByLabelText(/Pasaje de Ana/), { target: { value: '123' } })
    fireEvent.change(within(dialog).getByLabelText(/Pasaje de Leo/), { target: { value: '9301234567891' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar emisión' }))

    expect(await within(dialog).findByText('Cada número de pasaje tiene entre 6 y 20 caracteres.')).toBeTruthy()
    expect(recordIssuance).not.toHaveBeenCalled()
  })

  it('el localizador, si se carga, son 5 a 8 letras o números', async () => {
    renderWithProviders(<FlightIssuanceSection item={flightItem('CONFIRMED', 'PENDING')} />)
    fireEvent.click(screen.getByRole('button', { name: /Registrar emisión/ }))
    const dialog = await screen.findByRole('dialog')

    fireEvent.change(within(dialog).getByLabelText(/Pasaje de Ana/), { target: { value: '9301234567890' } })
    fireEvent.change(within(dialog).getByLabelText(/Pasaje de Leo/), { target: { value: '9301234567891' } })
    fireEvent.change(within(dialog).getByLabelText(/Localizador/), { target: { value: 'AB-1' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar emisión' }))

    expect(await within(dialog).findByText('El localizador son de 5 a 8 letras o números, por ejemplo ABC123.')).toBeTruthy()
    expect(recordIssuance).not.toHaveBeenCalled()
  })

  it('con el backend todavía en 501, el diálogo muestra el mensaje del servidor', async () => {
    recordIssuanceFailure.mockRejectedValue(notAvailable())
    renderWithProviders(<FlightIssuanceSection item={flightItem('CONFIRMED', 'PENDING')} />)

    fireEvent.click(screen.getByRole('button', { name: /No pude emitir/ }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText(/Qué pasó/), { target: { value: 'La aerolínea canceló la salida' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Registrar que no se emitió' }))

    expect(await within(dialog).findByText('Los vuelos de operador todavía no están disponibles en este servidor.')).toBeTruthy()
    expect(recordIssuanceFailure.mock.calls[0].slice(1)).toEqual(['item-1', { reason: 'La aerolínea canceló la salida' }])
  })

  it('emitido: muestra los números, como DEMO y sin acciones', () => {
    renderWithProviders(
      <FlightIssuanceSection
        item={flightItem('CONFIRMED', 'ISSUED', { ticketNumbers: ['9301234567890', '9301234567891'], bookingReference: 'ABC123' })}
      />,
    )

    expect(screen.getByText('Emitido')).toBeTruthy()
    expect(screen.getByText('9301234567891')).toBeTruthy()
    expect(screen.getByText(/no es un pasaje real/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Registrar emisión/ })).toBeNull()
  })
})
