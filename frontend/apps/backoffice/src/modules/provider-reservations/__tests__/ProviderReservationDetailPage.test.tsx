import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { reservationsApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ProviderReservationDetailPage } from '@/modules/provider-reservations/ProviderReservationDetailPage'

/**
 * UC-P-14 — el proveedor cancela su línea de forma excepcional. Lo que se fija: que la acción sólo aparece
 * en los estados que el backend acepta, que pide confirmación y motivo, que llama una sola vez aunque se
 * insista, y que el error del backend se muestra tal cual.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    reservationsApi: {
      ...actual.reservationsApi,
      getCompanyReservationById: vi.fn(),
      cancelCompanyReservation: vi.fn(),
    },
  }
})

const getItem = vi.mocked(reservationsApi.getCompanyReservationById)
const cancelItem = vi.mocked(reservationsApi.cancelCompanyReservation)
type Item = Awaited<ReturnType<typeof reservationsApi.cancelCompanyReservation>>

const ITEM = {
  id: 'item-1',
  reservationId: 'res-1',
  reservationStatus: 'CONFIRMED',
  experienceTitle: 'Trekking al glaciar',
  touristName: 'Ana Viajera',
  travelers: 2,
  unitPrice: 120,
  currency: 'USD',
  subtotal: 240,
  status: 'CONFIRMED',
  date: '2026-11-20',
  createdAt: '2026-10-01T12:00:00Z',
}

function setup(status: string) {
  getItem.mockResolvedValue({ ...ITEM, status })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/provider/reservations/item-1']}>
        <Routes>
          <Route path="/provider/reservations/:id" element={<ProviderReservationDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const REASON = 'Cierre del parque por alerta meteorológica'

async function openDialogAndType(reason: string) {
  fireEvent.click(await screen.findByRole('button', { name: 'Cancelar reserva' }))
  fireEvent.change(screen.getByLabelText(/Motivo/), { target: { value: reason } })
}

describe('ProviderReservationDetailPage — cancelación excepcional', () => {
  beforeEach(() => {
    getItem.mockReset()
    cancelItem.mockReset()
  })

  it.each(['CANCELLED', 'EXPIRED'])('no ofrece cancelar una línea %s', async (status) => {
    setup(status)
    await screen.findByText('Trekking al glaciar')
    expect(screen.queryByRole('button', { name: 'Cancelar reserva' })).toBeNull()
  })

  it.each(['CONFIRMED', 'PENDING_PAYMENT'])('ofrece cancelar una línea %s', async (status) => {
    setup(status)
    expect(await screen.findByRole('button', { name: 'Cancelar reserva' })).toBeTruthy()
  })

  it('pide confirmación, explica que es excepcional y exige motivo antes de llamar', async () => {
    setup('CONFIRMED')
    await openDialogAndType('ups')

    expect(screen.getByText(/medida excepcional/)).toBeTruthy()
    expect(screen.getByText(/no genera un reembolso automático/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Sí, cancelar reserva' }))

    expect(await screen.findByText('El motivo debe tener al menos 5 caracteres.')).toBeTruthy()
    expect(cancelItem).not.toHaveBeenCalled()
  })

  it('cancela una sola vez aunque se insista, y refresca el detalle', async () => {
    let resolve!: (value: Item) => void
    cancelItem.mockReturnValue(new Promise<Item>((r) => (resolve = r)))
    setup('CONFIRMED')
    await openDialogAndType(`  ${REASON}  `)

    const confirm = screen.getByRole('button', { name: 'Sí, cancelar reserva' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)
    expect(await screen.findByRole('button', { name: 'Cancelando…' })).toHaveProperty('disabled', true)

    const cancelled = { ...ITEM, status: 'CANCELLED', cancellationReason: REASON }
    getItem.mockResolvedValue(cancelled)
    resolve(cancelled)

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Cancelar reserva' })).toBeNull())
    expect(cancelItem).toHaveBeenCalledTimes(1)
    expect(cancelItem.mock.calls[0][1]).toBe('item-1')
    expect(cancelItem.mock.calls[0][2]).toEqual({ reason: REASON })
    expect(await screen.findByText(REASON)).toBeTruthy()
  })

  it('muestra el error del backend y deja reintentar', async () => {
    cancelItem.mockRejectedValue(
      new AxiosError('Request failed with status code 409', 'ERR_BAD_REQUEST', undefined, undefined, {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: { headers: new AxiosHeaders() },
        data: { detail: 'Una línea en estado CANCELLED no se puede cancelar.' },
      }),
    )
    setup('CONFIRMED')
    await openDialogAndType(REASON)

    fireEvent.click(screen.getByRole('button', { name: 'Sí, cancelar reserva' }))

    expect(await screen.findByText('Una línea en estado CANCELLED no se puede cancelar.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Sí, cancelar reserva' })).toHaveProperty('disabled', false)
  })
})
