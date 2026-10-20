import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { AdminExperiencesPage, AdminPackagesPage } from '@/modules/platform/AdminCatalogPages'

/**
 * Moderación del catálogo (UC-A-07). Lo que se fija: que suspender y levantar la suspensión piden confirmación y
 * explican el efecto, que llaman al endpoint correcto una sola vez aunque se insista, que la tabla se refresca con
 * el estado nuevo y que un rechazo del servidor se muestra sin cerrar el diálogo.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    adminApi: {
      ...actual.adminApi,
      listExperiences: vi.fn(),
      listPackages: vi.fn(),
      suspendExperience: vi.fn(),
      restoreExperience: vi.fn(),
      suspendPackage: vi.fn(),
      restorePackage: vi.fn(),
    },
  }
})

const listExperiences = vi.mocked(adminApi.listExperiences)
const listPackages = vi.mocked(adminApi.listPackages)
const suspendExperience = vi.mocked(adminApi.suspendExperience)
const restoreExperience = vi.mocked(adminApi.restoreExperience)
const suspendPackage = vi.mocked(adminApi.suspendPackage)

const experience = (id: string, title: string, status: string) => ({
  id,
  title,
  status,
  companyName: 'Andes Tours',
  companyStatus: 'APPROVED',
  destinationName: 'Uyuni',
  price: 100,
  currency: 'BOB',
  futureAvailabilities: 3,
})

const pkg = (id: string, title: string, status: string) => ({
  id,
  title,
  status,
  companyName: 'Andes Tours',
  destinationName: 'Uyuni',
  durationDays: 3,
  includesFlight: false,
  price: 900,
  currency: 'BOB',
  futureDepartures: 2,
  hasCancellationPolicy: true,
})

const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 })

function renderPage(page: React.ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>{page}</MemoryRouter>
    </QueryClientProvider>,
  )
}

const row = (title: string) => screen.getByText(title).closest('tr') as HTMLElement

const conflict = (detail: string) =>
  new AxiosError('Request failed with status code 409', 'ERR_BAD_REQUEST', undefined, undefined, {
    status: 409,
    statusText: 'Conflict',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data: { detail },
  })

describe('AdminExperiencesPage — moderación', () => {
  it('suspender pide confirmación, explica el efecto y se puede cancelar sin llamar al servidor', async () => {
    listExperiences.mockResolvedValue(paged([experience('e1', 'Salar al amanecer', 'PUBLISHED')]))
    renderPage(<AdminExperiencesPage />)

    await screen.findByText('Salar al amanecer')
    fireEvent.click(within(row('Salar al amanecer')).getByRole('button', { name: /Suspender/ }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('¿Suspender la experiencia?')).toBeTruthy()
    expect(within(dialog).getByText(/deja de verse en el catálogo/)).toBeTruthy()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(suspendExperience).not.toHaveBeenCalled()
  })

  it('al confirmar suspende una sola vez y la tabla muestra el estado nuevo', async () => {
    listExperiences
      .mockResolvedValueOnce(paged([experience('e1', 'Salar al amanecer', 'PUBLISHED')]))
      .mockResolvedValue(paged([experience('e1', 'Salar al amanecer', 'SUSPENDED')]))
    let finish: (value: Awaited<ReturnType<typeof adminApi.suspendExperience>>) => void = () => {}
    suspendExperience.mockImplementation(() => new Promise((resolve) => (finish = resolve)))
    renderPage(<AdminExperiencesPage />)

    await screen.findByText('Salar al amanecer')
    expect(within(row('Salar al amanecer')).getByText('Publicado')).toBeTruthy()
    fireEvent.click(within(row('Salar al amanecer')).getByRole('button', { name: /Suspender/ }))

    const dialog = await screen.findByRole('dialog')
    const confirm = within(dialog).getByRole('button', { name: 'Suspender' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)
    await waitFor(() => expect(suspendExperience).toHaveBeenCalledTimes(1))
    expect(suspendExperience).toHaveBeenLastCalledWith(expect.anything(), 'e1')
    await waitFor(() => expect((confirm as HTMLButtonElement).disabled).toBe(true))

    finish({ id: 'e1', status: 'SUSPENDED' })

    expect(await screen.findByText('«Salar al amanecer» quedó suspendida.')).toBeTruthy()
    await waitFor(() => expect(within(row('Salar al amanecer')).getByText('Suspendido')).toBeTruthy())
    expect(listExperiences.mock.calls.length).toBeGreaterThanOrEqual(2)
    expect(suspendExperience).toHaveBeenCalledTimes(1)
    expect(within(row('Salar al amanecer')).getByRole('button', { name: /Levantar suspensión/ })).toBeTruthy()
  })

  it('una experiencia suspendida se libera con restore y queda despublicada', async () => {
    listExperiences
      .mockResolvedValueOnce(paged([experience('e2', 'Isla del Sol', 'SUSPENDED')]))
      .mockResolvedValue(paged([experience('e2', 'Isla del Sol', 'UNPUBLISHED')]))
    restoreExperience.mockResolvedValue({ id: 'e2', status: 'UNPUBLISHED' })
    renderPage(<AdminExperiencesPage />)

    await screen.findByText('Isla del Sol')
    expect(within(row('Isla del Sol')).queryByRole('button', { name: /^Suspender/ })).toBeNull()
    fireEvent.click(within(row('Isla del Sol')).getByRole('button', { name: /Levantar suspensión/ }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/no se publica sola/)).toBeTruthy()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Levantar suspensión' }))

    await waitFor(() => expect(within(row('Isla del Sol')).getByText('Despublicado')).toBeTruthy())
    expect(restoreExperience).toHaveBeenCalledWith(expect.anything(), 'e2')
    expect(suspendExperience).not.toHaveBeenCalled()
  })
})

describe('AdminPackagesPage — moderación', () => {
  it('si el servidor rechaza la suspensión, muestra el motivo y deja el diálogo abierto', async () => {
    listPackages.mockResolvedValue(paged([pkg('p1', 'Salar 3 días', 'PUBLISHED')]))
    suspendPackage.mockRejectedValue(conflict('Paquete no encontrado.'))
    renderPage(<AdminPackagesPage />)

    await screen.findByText('Salar 3 días')
    fireEvent.click(within(row('Salar 3 días')).getByRole('button', { name: /Suspender/ }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('¿Suspender el paquete?')).toBeTruthy()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Suspender' }))

    expect(await within(dialog).findByText('Paquete no encontrado.')).toBeTruthy()
    expect(suspendPackage).toHaveBeenCalledWith(expect.anything(), 'p1')
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(within(row('Salar 3 días')).getByText('Publicado')).toBeTruthy()
  })
})
