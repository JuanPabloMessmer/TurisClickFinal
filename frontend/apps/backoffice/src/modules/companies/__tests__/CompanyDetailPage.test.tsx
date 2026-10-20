import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { adminApi, companiesApi, type CompanyResponse } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { CompanyDetailPage } from '@/modules/companies/CompanyDetailPage'

/**
 * Suspender y reactivar una empresa desde su detalle (UC-A-08). Lo que se fija: cada acción aparece solo en el
 * estado que el backend acepta, siempre pasa por una confirmación, viaja una sola vez aunque se haga doble clic,
 * y el error del servidor llega a la pantalla.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    companiesApi: {
      ...actual.companiesApi,
      getCompanyById: vi.fn(),
      suspendCompany: vi.fn(),
      reactivateCompany: vi.fn(),
    },
    adminApi: { ...actual.adminApi, listCompanyUsers: vi.fn() },
  }
})

const getCompanyById = vi.mocked(companiesApi.getCompanyById)
const suspendCompany = vi.mocked(companiesApi.suspendCompany)
const reactivateCompany = vi.mocked(companiesApi.reactivateCompany)

const company = (status: string): CompanyResponse => ({
  id: 'c1',
  name: 'Andes Trekking',
  legalDocument: '30-12345678-9',
  contactEmail: 'hola@andes.example.com',
  status,
})

function conflict(detail: string) {
  return new AxiosError('Request failed with status code 409', 'ERR_BAD_REQUEST', undefined, undefined, {
    status: 409,
    statusText: 'Conflict',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data: { detail },
  })
}

function setup(status: string) {
  getCompanyById.mockResolvedValue(company(status))
  vi.mocked(adminApi.listCompanyUsers).mockResolvedValue([])

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/companies/c1']}>
        <Routes>
          <Route path="/admin/companies/:id" element={<CompanyDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('CompanyDetailPage — suspensión', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('una empresa aprobada se puede suspender y no se puede reactivar', async () => {
    setup('APPROVED')

    expect(await screen.findByRole('button', { name: 'Suspender empresa' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Reactivar empresa' })).toBeNull()
    expect(screen.queryByText(/Todavía no se puede hacer desde acá/)).toBeNull()
  })

  it('al confirmar suspende una sola vez y el estado pasa a Suspendida', async () => {
    suspendCompany.mockResolvedValue(company('SUSPENDED'))
    setup('APPROVED')

    fireEvent.click(await screen.findByRole('button', { name: 'Suspender empresa' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByText(/dejan de verse para los viajeros y para el asistente/)).toBeTruthy()
    expect(within(dialog).getByText(/Las reservas que ya existen se mantienen/)).toBeTruthy()
    expect(suspendCompany).not.toHaveBeenCalled()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Suspender' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(suspendCompany).toHaveBeenCalledTimes(1)
    expect(suspendCompany).toHaveBeenCalledWith(expect.anything(), 'c1')
    expect(screen.getByText('Suspendida')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Reactivar empresa' })).toBeTruthy()
  })

  it('cancelar no llama al servidor', async () => {
    setup('APPROVED')

    fireEvent.click(await screen.findByRole('button', { name: 'Suspender empresa' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancelar' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(suspendCompany).not.toHaveBeenCalled()
  })

  it('el doble clic en confirmar no suspende dos veces', async () => {
    let resolve: (value: CompanyResponse) => void = () => {}
    suspendCompany.mockImplementation(() => new Promise<CompanyResponse>((r) => (resolve = r)))
    setup('APPROVED')

    fireEvent.click(await screen.findByRole('button', { name: 'Suspender empresa' }))
    const confirm = within(screen.getByRole('dialog')).getByRole('button', { name: 'Suspender' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)

    await waitFor(() => expect(confirm.hasAttribute('disabled')).toBe(true))
    fireEvent.click(confirm)
    resolve(company('SUSPENDED'))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(suspendCompany).toHaveBeenCalledTimes(1)
  })

  it('una empresa suspendida se puede reactivar', async () => {
    reactivateCompany.mockResolvedValue(company('APPROVED'))
    setup('SUSPENDED')

    fireEvent.click(await screen.findByRole('button', { name: 'Reactivar empresa' }))
    expect(screen.queryByRole('button', { name: 'Suspender empresa' })).toBeNull()
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Reactivar' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(reactivateCompany).toHaveBeenCalledTimes(1)
    // "Aprobada" también es el rótulo de la fecha de aprobación: el estado se lee por la ausencia de "Suspendida".
    expect(screen.queryByText('Suspendida')).toBeNull()
    expect(screen.getByRole('button', { name: 'Suspender empresa' })).toBeTruthy()
  })

  it('una empresa pendiente de aprobación no se puede suspender ni reactivar', async () => {
    setup('PENDING_APPROVAL')

    expect(await screen.findByRole('button', { name: 'Aprobar' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Suspender empresa' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reactivar empresa' })).toBeNull()
  })

  it('si el servidor responde 409, muestra el motivo y deja el diálogo abierto', async () => {
    suspendCompany.mockRejectedValue(conflict('Solo se puede suspender una empresa aprobada.'))
    setup('APPROVED')

    fireEvent.click(await screen.findByRole('button', { name: 'Suspender empresa' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Suspender' }))

    expect(await screen.findByText('Solo se puede suspender una empresa aprobada.')).toBeTruthy()
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(screen.queryByText('Suspendida')).toBeNull()
  })
})
