import { adminApi } from '@turisclick/api-client'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderWithProviders } from '@/modules/provider-flights/__tests__/testUtils'
import { AdminReservationsPage, needsAttention } from '../AdminReservationsPage'

/** Reservas de vuelos de operador en la cola del administrador: el pasaje existe sólo cuando se registra la emisión. */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    adminApi: { ...actual.adminApi, listReservations: vi.fn(), listUnresolvedCancellations: vi.fn() },
  }
})
const listReservations = vi.mocked(adminApi.listReservations)
const listUnresolvedCancellations = vi.mocked(adminApi.listUnresolvedCancellations)

const ROW = {
  id: 'r1',
  status: 'CONFIRMED',
  kind: 'FLIGHT',
  summary: 'Vuelo MIA → VVI',
  touristName: 'Ana Prueba',
  companies: ['Altura Viva'],
  flightIssuancePending: true,
  flightIssuanceOverdue: false,
}

beforeEach(() => {
  vi.clearAllMocks()
  listUnresolvedCancellations.mockResolvedValue([])
})

describe('AdminReservationsPage con vuelos de operador', () => {
  it('muestra la emisión pendiente sin decir que hay pasaje, y la atrasada como alerta', async () => {
    listReservations.mockResolvedValue({
      items: [ROW, { ...ROW, id: 'r2', flightIssuanceOverdue: true }],
      page: 1,
      pageSize: 20,
      totalCount: 2,
      totalPages: 1,
    })
    renderWithProviders(<AdminReservationsPage />)

    expect(await screen.findByText('Emisión pendiente del operador')).toBeTruthy()
    expect(screen.getByText('Sin emitir hace más de 24 h')).toBeTruthy()
    expect(screen.queryByText(/Pasaje emitido/i)).toBeNull()
  })

  it('filtra por vuelos de operador y combina con "Necesita atención"', async () => {
    listReservations.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 1 })
    renderWithProviders(<AdminReservationsPage />, '/?product=FLIGHT')

    await waitFor(() => expect(listReservations).toHaveBeenCalled())
    expect(listReservations.mock.calls.at(-1)?.[1]).toMatchObject({ productType: 'FLIGHT' })
    fireEvent.click(screen.getByRole('button', { name: /Necesita atención/ }))
    await waitFor(() =>
      expect(listReservations.mock.calls.at(-1)?.[1]).toMatchObject({ productType: 'FLIGHT', needsAttention: true }),
    )
  })

  it('un vuelo sin emitir hace más de 24 h necesita atención', () => {
    expect(needsAttention({ flightIssuanceOverdue: true })).toBe(true)
    expect(needsAttention({ flightIssuanceOverdue: false })).toBe(false)
  })
})
