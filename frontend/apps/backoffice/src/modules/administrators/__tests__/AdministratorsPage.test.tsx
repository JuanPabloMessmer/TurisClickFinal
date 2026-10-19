import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { useAuth } from '@/auth/useAuth'
import { AdministratorsPage } from '@/modules/administrators/AdministratorsPage'

/**
 * La lista de administradores y la regeneración de credenciales. Lo que se fija: que regenerar pide confirmación,
 * que la contraseña nueva se ve una sola vez y nunca antes de que el servidor confirme, y que la propia cuenta no
 * se puede regenerar desde acá.
 */
vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    adminApi: { ...actual.adminApi, listAdministrators: vi.fn(), resetAdminPassword: vi.fn() },
  }
})

const listAdministrators = vi.mocked(adminApi.listAdministrators)
const resetAdminPassword = vi.mocked(adminApi.resetAdminPassword)
const NEW_PASSWORD = 'WXYZ-2345-PQRS-a1'

const SELF = { id: 'self', firstName: 'Juan', lastName: 'Admin', email: 'juan@example.com', role: 'ADMIN', status: 'ACTIVE', mustChangePassword: false }
const OTHER = { id: 'other', firstName: 'Ricardo', lastName: 'Prueba', email: 'ricardo@example.com', role: 'ADMIN', status: 'ACTIVE', mustChangePassword: false }
const PENDING = { id: 'pending', firstName: 'Ana', lastName: 'Nueva', email: 'ana@example.com', role: 'ADMIN', status: 'ACTIVE', mustChangePassword: true }

function setup() {
  vi.mocked(useAuth).mockReturnValue({
    status: 'authenticated',
    user: { id: 'self', role: 'ADMIN', fullName: 'Juan Admin' },
    login: vi.fn(),
    logout: vi.fn(),
  } as unknown as ReturnType<typeof useAuth>)
  listAdministrators.mockResolvedValue({ items: [SELF, OTHER, PENDING], page: 1, pageSize: 100, totalCount: 3 })

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdministratorsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const row = (email: string) => screen.getByText(email).closest('li') as HTMLElement

describe('AdministratorsPage', () => {
  it('lista los administradores y marca a quien todavía tiene la contraseña temporal', async () => {
    setup()

    expect(await screen.findByText('ricardo@example.com')).toBeTruthy()
    expect(within(row('ana@example.com')).getByText('Contraseña temporal pendiente')).toBeTruthy()
    expect(within(row('ricardo@example.com')).queryByText('Contraseña temporal pendiente')).toBeNull()
    expect(screen.getByRole('link', { name: /Dar de alta un administrador/ }).getAttribute('href')).toBe(
      '/admin/administrators/new',
    )
  })

  it('la propia cuenta no se puede regenerar desde acá', async () => {
    setup()

    await screen.findByText('juan@example.com')
    const own = row('juan@example.com')
    expect(within(own).getByText('(vos)')).toBeTruthy()
    expect(within(own).queryByRole('button', { name: 'Regenerar contraseña' })).toBeNull()
  })

  it('regenerar pide confirmación y se puede cancelar sin llamar al servidor', async () => {
    setup()
    await screen.findByText('ricardo@example.com')
    const callsBefore = resetAdminPassword.mock.calls.length

    fireEvent.click(within(row('ricardo@example.com')).getByRole('button', { name: 'Regenerar contraseña' }))
    expect(within(row('ricardo@example.com')).getByText('Se cierran sus sesiones.')).toBeTruthy()
    fireEvent.click(within(row('ricardo@example.com')).getByRole('button', { name: 'Cancelar' }))

    expect(resetAdminPassword.mock.calls.length).toBe(callsBefore)
    expect(within(row('ricardo@example.com')).getByRole('button', { name: 'Regenerar contraseña' })).toBeTruthy()
  })

  it('al confirmar muestra la contraseña nueva una sola vez', async () => {
    resetAdminPassword.mockResolvedValue({
      userId: 'other',
      email: 'ricardo@example.com',
      temporaryPassword: NEW_PASSWORD,
      mustChangePassword: true,
    })
    setup()
    await screen.findByText('ricardo@example.com')
    expect(screen.queryByText(NEW_PASSWORD)).toBeNull()

    fireEvent.click(within(row('ricardo@example.com')).getByRole('button', { name: 'Regenerar contraseña' }))
    fireEvent.click(within(row('ricardo@example.com')).getByRole('button', { name: 'Confirmar' }))

    expect(await screen.findByText(NEW_PASSWORD)).toBeTruthy()
    expect(resetAdminPassword).toHaveBeenLastCalledWith(expect.anything(), 'other')
    expect(screen.getByText('Contraseña nueva para ricardo@example.com')).toBeTruthy()
    expect(screen.getByText(/Se muestra una sola vez/)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Listo, ya la entregué' }))
    expect(screen.queryByText(NEW_PASSWORD)).toBeNull()
  })

  it('si el servidor lo rechaza, informa el motivo sin mostrar ninguna contraseña', async () => {
    resetAdminPassword.mockImplementation(() =>
      Promise.reject(
        new AxiosError('Request failed with status code 400', 'ERR_BAD_REQUEST', undefined, undefined, {
          status: 400,
          statusText: 'Bad Request',
          headers: {},
          config: { headers: new AxiosHeaders() },
          data: { detail: 'Sólo se puede regenerar la credencial de una cuenta de administrador.' },
        }),
      ),
    )
    setup()
    await screen.findByText('ricardo@example.com')

    fireEvent.click(within(row('ricardo@example.com')).getByRole('button', { name: 'Regenerar contraseña' }))
    fireEvent.click(within(row('ricardo@example.com')).getByRole('button', { name: 'Confirmar' }))

    expect(
      await screen.findByText('Sólo se puede regenerar la credencial de una cuenta de administrador.'),
    ).toBeTruthy()
    expect(screen.queryByText(/Contraseña nueva para/)).toBeNull()
  })
})
