import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { adminApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { NewAdminAccountPage } from '@/modules/administrators/NewAdminAccountPage'

/**
 * Alta de administradores desde el Backoffice. La regla que se fija acá es la del alta de operadores: la
 * contraseña temporal se ve una sola vez, nunca antes de que el servidor confirme, y un email repetido se
 * informa sin mostrar ninguna credencial.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return { ...actual, adminApi: { ...actual.adminApi, createAdminAccount: vi.fn() } }
})
const createAdminAccount = vi.mocked(adminApi.createAdminAccount)

const TEMPORARY = 'ABCD-EFGH-JKMN-a1'

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NewAdminAccountPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

function fill(firstName: string, lastName: string, email: string) {
  fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: firstName } })
  fireEvent.change(screen.getByLabelText('Apellido'), { target: { value: lastName } })
  fireEvent.change(screen.getByLabelText('Correo con el que va a entrar'), { target: { value: email } })
  fireEvent.click(screen.getByRole('button', { name: 'Dar de alta y generar credenciales' }))
}

function conflict(detail: string) {
  return new AxiosError('Request failed with status code 409', 'ERR_BAD_REQUEST', undefined, undefined, {
    status: 409,
    statusText: 'Conflict',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data: { title: 'Conflicto', status: 409, detail },
  })
}

describe('NewAdminAccountPage', () => {
  it('pide los tres datos antes de llamar al servidor', async () => {
    renderPage()
    const callsBefore = createAdminAccount.mock.calls.length

    fill('', '', 'no-es-un-correo')

    expect(await screen.findByText('Ingresá el nombre.')).toBeTruthy()
    expect(screen.getByText('Ingresá el apellido.')).toBeTruthy()
    expect(screen.getByText('Ingresá el correo con el que va a entrar.')).toBeTruthy()
    expect(createAdminAccount.mock.calls.length).toBe(callsBefore)
  })

  it('no pide contraseña: la genera el servidor', () => {
    renderPage()

    expect(document.querySelector('input[type="password"]')).toBeNull()
    expect(screen.queryByLabelText(/contraseña/i)).toBeNull()
  })

  it('manda los datos sin espacios sobrantes y muestra la credencial temporal una sola vez', async () => {
    createAdminAccount.mockResolvedValue({
      userId: 'u1',
      email: 'ricardo@example.com',
      fullName: 'Ricardo Prueba',
      temporaryPassword: TEMPORARY,
      mustChangePassword: true,
    })
    renderPage()

    expect(screen.queryByText(TEMPORARY)).toBeNull()
    fill('  Ricardo ', 'Prueba  ', ' ricardo@example.com ')

    expect(await screen.findByText(TEMPORARY)).toBeTruthy()
    expect(createAdminAccount).toHaveBeenCalledWith(expect.anything(), {
      firstName: 'Ricardo',
      lastName: 'Prueba',
      email: 'ricardo@example.com',
    })
    expect(screen.getByText('ricardo@example.com')).toBeTruthy()
    expect(screen.getByText(/se muestra una sola vez/)).toBeTruthy()
    expect(screen.getByText(/va a tener que elegir una contraseña propia/)).toBeTruthy()
  })

  it('al dar de alta otro, la credencial anterior desaparece de la pantalla', async () => {
    createAdminAccount.mockResolvedValue({
      userId: 'u1',
      email: 'ricardo@example.com',
      fullName: 'Ricardo Prueba',
      temporaryPassword: TEMPORARY,
      mustChangePassword: true,
    })
    renderPage()
    fill('Ricardo', 'Prueba', 'ricardo@example.com')
    await screen.findByText(TEMPORARY)

    fireEvent.click(screen.getByRole('button', { name: 'Dar de alta otro administrador' }))

    expect(screen.queryByText(TEMPORARY)).toBeNull()
    expect(screen.getByRole('button', { name: 'Dar de alta y generar credenciales' })).toBeTruthy()
  })

  it('un email ya registrado se informa sin mostrar ninguna credencial', async () => {
    createAdminAccount.mockRejectedValue(conflict('Ya existe una cuenta con ese email.'))
    renderPage()
    const callsBefore = createAdminAccount.mock.calls.length

    fill('Ricardo', 'Prueba', 'existente@example.com')

    expect(await screen.findByText('Ya existe una cuenta con ese email.')).toBeTruthy()
    expect(screen.queryByText('Credenciales para entregar')).toBeNull()
    // Un solo intento: el error no se reintenta solo ni dispara un segundo alta.
    await waitFor(() => expect(createAdminAccount.mock.calls.length).toBe(callsBefore + 1))
  })
})
