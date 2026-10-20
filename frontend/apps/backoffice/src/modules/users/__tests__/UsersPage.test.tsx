import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { useAuth } from '@/auth/useAuth'
import { UsersPage } from '@/modules/users/UsersPage'

/**
 * Gestión de cuentas (UC-A-06). Lo que se fija: que los filtros de la URL llegan al endpoint, que suspender y
 * activar piden confirmación y refrescan la lista, que la propia cuenta no se ofrece para suspender y que un
 * rechazo del servidor se muestra.
 */
vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    adminApi: { ...actual.adminApi, listUsers: vi.fn(), suspendUser: vi.fn(), activateUser: vi.fn() },
  }
})

const listUsers = vi.mocked(adminApi.listUsers)
const suspendUser = vi.mocked(adminApi.suspendUser)
const activateUser = vi.mocked(adminApi.activateUser)

const SELF = { id: 'self', firstName: 'Juan', lastName: 'Admin', email: 'juan@example.com', role: 'ADMIN', status: 'ACTIVE' }
const TOURIST = { id: 'tourist', firstName: 'Lucía', lastName: 'Viajera', email: 'lucia@example.com', role: 'TOURIST', status: 'ACTIVE' }
const PROVIDER = {
  id: 'provider',
  firstName: 'Pedro',
  lastName: 'Operador',
  email: 'pedro@example.com',
  role: 'PROVIDER',
  status: 'SUSPENDED',
  companyName: 'Andes Tours',
}

const paged = <T,>(items: T[], totalPages = 1) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages })

function setup(url = '/admin/users') {
  vi.mocked(useAuth).mockReturnValue({
    status: 'authenticated',
    user: { id: 'self', role: 'ADMIN', fullName: 'Juan Admin' },
    login: vi.fn(),
    logout: vi.fn(),
  } as unknown as ReturnType<typeof useAuth>)

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[url]}>
        <UsersPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const row = (email: string) => screen.getByText(email).closest('tr') as HTMLElement

describe('UsersPage', () => {
  it('sin filtros pide todas las cuentas, de todos los roles', async () => {
    listUsers.mockResolvedValue(paged([SELF, TOURIST, PROVIDER]))
    setup()

    await screen.findByText('lucia@example.com')
    expect(listUsers).toHaveBeenLastCalledWith(expect.anything(), {
      role: undefined,
      status: undefined,
      search: undefined,
      page: 1,
      pageSize: 20,
    })
    expect(within(row('lucia@example.com')).getByText('Viajero')).toBeTruthy()
    expect(within(row('pedro@example.com')).getByText('Andes Tours')).toBeTruthy()
  })

  it('los filtros de la URL llegan al endpoint', async () => {
    listUsers.mockResolvedValue(paged([PROVIDER], 3))
    setup('/admin/users?role=PROVIDER&status=SUSPENDED&search=pedro&page=2')

    await screen.findByText('pedro@example.com')
    expect(listUsers).toHaveBeenLastCalledWith(expect.anything(), {
      role: 'PROVIDER',
      status: 'SUSPENDED',
      search: 'pedro',
      page: 2,
      pageSize: 20,
    })
    expect(screen.getByText('Página 2 de 3')).toBeTruthy()
  })

  it('la búsqueda escrita se manda al endpoint y vuelve a la primera página', async () => {
    listUsers.mockResolvedValue(paged([TOURIST]))
    setup('/admin/users?page=2')

    await screen.findByText('lucia@example.com')
    fireEvent.change(screen.getByLabelText('Buscar usuarios'), { target: { value: 'lucia' } })

    await waitFor(() =>
      expect(listUsers).toHaveBeenLastCalledWith(expect.anything(), expect.objectContaining({ search: 'lucia', page: 1 })),
    )
  })

  it('la propia cuenta no se ofrece para suspender', async () => {
    listUsers.mockResolvedValue(paged([SELF, TOURIST]))
    setup()

    await screen.findByText('juan@example.com')
    const own = row('juan@example.com')
    expect(within(own).getByText('(vos)')).toBeTruthy()
    expect(within(own).queryByRole('button')).toBeNull()
    expect(within(row('lucia@example.com')).getByRole('button', { name: /Suspender/ })).toBeTruthy()
  })

  it('suspender pide confirmación, llama una vez y refresca la lista', async () => {
    listUsers
      .mockResolvedValueOnce(paged([SELF, TOURIST]))
      .mockResolvedValue(paged([SELF, { ...TOURIST, status: 'SUSPENDED' }]))
    suspendUser.mockResolvedValue({ ...TOURIST, status: 'SUSPENDED' })
    setup()

    await screen.findByText('lucia@example.com')
    fireEvent.click(within(row('lucia@example.com')).getByRole('button', { name: /Suspender/ }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/no va a poder ingresar ni renovar su sesión/)).toBeTruthy()

    const confirm = within(dialog).getByRole('button', { name: 'Suspender' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)

    expect(await screen.findByText('La cuenta de Lucía Viajera quedó suspendida.')).toBeTruthy()
    expect(suspendUser).toHaveBeenCalledTimes(1)
    expect(suspendUser).toHaveBeenCalledWith(expect.anything(), 'tourist')
    await waitFor(() => expect(within(row('lucia@example.com')).getByText('Suspendida')).toBeTruthy())
    expect(within(row('lucia@example.com')).getByRole('button', { name: /Activar/ })).toBeTruthy()
  })

  it('una cuenta suspendida se reactiva', async () => {
    listUsers
      .mockResolvedValueOnce(paged([PROVIDER]))
      .mockResolvedValue(paged([{ ...PROVIDER, status: 'ACTIVE' }]))
    activateUser.mockResolvedValue({ ...PROVIDER, status: 'ACTIVE' })
    setup()

    await screen.findByText('pedro@example.com')
    fireEvent.click(within(row('pedro@example.com')).getByRole('button', { name: /Activar/ }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Activar' }))

    await waitFor(() => expect(within(row('pedro@example.com')).getByText('Activa')).toBeTruthy())
    expect(activateUser).toHaveBeenCalledWith(expect.anything(), 'provider')
    expect(suspendUser).not.toHaveBeenCalled()
  })

  it('si el servidor lo rechaza, muestra el motivo en el diálogo', async () => {
    // Una cuenta propia suspendida sí muestra "Activar"; acá se fuerza el rechazo del backend sobre otra.
    listUsers.mockResolvedValue(paged([TOURIST]))
    suspendUser.mockRejectedValue(
      new AxiosError('Request failed with status code 409', 'ERR_BAD_REQUEST', undefined, undefined, {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: { headers: new AxiosHeaders() },
        data: { detail: 'No podés suspender tu propia cuenta.' },
      }),
    )
    setup()

    await screen.findByText('lucia@example.com')
    fireEvent.click(within(row('lucia@example.com')).getByRole('button', { name: /Suspender/ }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Suspender' }))

    expect(await within(dialog).findByText('No podés suspender tu propia cuenta.')).toBeTruthy()
    expect(within(row('lucia@example.com')).getByText('Activa')).toBeTruthy()
  })
})
