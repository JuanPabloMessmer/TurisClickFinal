import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { UnresolvedCancellationsPanel } from '@/modules/platform/UnresolvedCancellationsPanel'

/**
 * "Reintentar ahora" (UC-A-10): dispara la resolución una sola vez aunque se insista, refresca la cola y dice
 * cuántas se resolvieron; si falla, lo dice sin vaciar la lista.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    adminApi: { ...actual.adminApi, listUnresolvedCancellations: vi.fn(), resolveCancellations: vi.fn() },
  }
})

const listUnresolved = vi.mocked(adminApi.listUnresolvedCancellations)
const resolveCancellations = vi.mocked(adminApi.resolveCancellations)

const PENDING = {
  id: 'c1',
  reservationId: 'r1',
  status: 'REFUND_PENDING',
  createdAt: '2026-10-01T12:00:00Z',
  resolutionAttempts: 2,
  failureReason: 'El procesador de pagos no respondió.',
}

function setup() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <UnresolvedCancellationsPanel />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('UnresolvedCancellationsPanel', () => {
  it('lista las cancelaciones a medias con su enlace al detalle', async () => {
    listUnresolved.mockResolvedValue([PENDING])
    setup()

    expect(await screen.findByText('Reembolso pendiente')).toBeTruthy()
    expect(screen.getByText('El procesador de pagos no respondió.')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Ver pagos' }).getAttribute('href')).toBe('/admin/reservations/r1')
  })

  it('reintentar llama una sola vez, refresca la cola y muestra el resultado', async () => {
    listUnresolved.mockResolvedValueOnce([PENDING]).mockResolvedValue([])
    let finish: (value: Awaited<ReturnType<typeof adminApi.resolveCancellations>>) => void = () => {}
    resolveCancellations.mockImplementation(() => new Promise((resolve) => (finish = resolve)))
    setup()

    await screen.findByText('Reembolso pendiente')
    const button = screen.getByRole('button', { name: /Reintentar ahora/ })
    fireEvent.click(button)
    fireEvent.click(button)
    await waitFor(() => expect(resolveCancellations).toHaveBeenCalledTimes(1))
    await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(true))

    finish({ attempted: 1, completed: 1, stillPending: 0 })

    expect(await screen.findByText('Se resolvieron 1 de 1. No queda ninguna pendiente.')).toBeTruthy()
    expect(await screen.findByText('Ninguna cancelación quedó a medias.')).toBeTruthy()
    expect(listUnresolved.mock.calls.length).toBeGreaterThanOrEqual(2)
    expect(resolveCancellations).toHaveBeenCalledTimes(1)
  })

  it('si el reintento falla, informa el motivo y la cola sigue visible', async () => {
    listUnresolved.mockResolvedValue([PENDING])
    resolveCancellations.mockRejectedValue(
      new AxiosError('Request failed with status code 500', 'ERR_BAD_RESPONSE', undefined, undefined, {
        status: 500,
        statusText: 'Internal Server Error',
        headers: {},
        config: { headers: new AxiosHeaders() },
        data: { title: 'No se pudo contactar al procesador de pagos.' },
      }),
    )
    setup()

    await screen.findByText('Reembolso pendiente')
    fireEvent.click(screen.getByRole('button', { name: /Reintentar ahora/ }))

    expect(await screen.findByText('No se pudo contactar al procesador de pagos.')).toBeTruthy()
    expect(screen.getByText('Reembolso pendiente')).toBeTruthy()
  })

  it('sin pendientes no ofrece reintentar', async () => {
    listUnresolved.mockResolvedValue([])
    setup()

    expect(await screen.findByText('Ninguna cancelación quedó a medias.')).toBeTruthy()
    expect((screen.getByRole('button', { name: /Reintentar ahora/ }) as HTMLButtonElement).disabled).toBe(true)
  })
})
