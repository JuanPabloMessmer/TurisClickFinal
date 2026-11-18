import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { companiesApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { MyCompanyPage } from '@/modules/my-company/MyCompanyPage'

/**
 * Mi empresa (R-01, R-02, R-04, R-05). Antes, los errores de descripción y teléfono no se mostraban y el envío se
 * bloqueaba sin explicación; y un dato viejo inválido tiene que señalarse para que el operador lo corrija.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return { ...actual, companiesApi: { ...actual.companiesApi, getMyCompany: vi.fn(), updateMyCompany: vi.fn() } }
})
const getMyCompany = vi.mocked(companiesApi.getMyCompany)
const updateMyCompany = vi.mocked(companiesApi.updateMyCompany)

const COMPANY = {
  id: 'c1',
  name: 'Andes Tours',
  description: 'Excursiones.',
  legalDocument: 'NIT-DEMO-1020304',
  contactEmail: 'contacto@andes.bo',
  contactPhone: 'llamar al 7123',
  status: 'APPROVED',
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <MyCompanyPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('MyCompanyPage', () => {
  it('un teléfono viejo inválido se señala debajo del campo y no se envía hasta corregirlo', async () => {
    getMyCompany.mockResolvedValue(COMPANY as never)
    renderPage()
    await screen.findByDisplayValue('Andes Tours')

    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }))

    expect(await screen.findByText('El teléfono no puede tener letras.')).toBeTruthy()
    expect(updateMyCompany).not.toHaveBeenCalled()
  })

  it('muestra el error y el contador de la descripción', async () => {
    getMyCompany.mockResolvedValue({ ...COMPANY, contactPhone: '71234567' } as never)
    renderPage()
    await screen.findByDisplayValue('Andes Tours')

    fireEvent.change(screen.getByLabelText('Descripción (opcional)'), { target: { value: 'a'.repeat(1001) } })
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }))

    expect(await screen.findByText('La descripción puede tener hasta 1000 caracteres (tiene 1001).')).toBeTruthy()
    expect(screen.getByText('1001/1000')).toBeTruthy()
  })

  it('un nombre tomado por otra empresa vuelve del servidor y queda debajo del nombre', async () => {
    getMyCompany.mockResolvedValue({ ...COMPANY, contactPhone: '71234567' } as never)
    const { AxiosError, AxiosHeaders } = await import('axios')
    updateMyCompany.mockRejectedValue(
      new AxiosError('Request failed', 'ERR_BAD_REQUEST', undefined, undefined, {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: { headers: new AxiosHeaders() },
        data: { detail: 'Ya existe una empresa con ese nombre: «Altura Viva».', errors: { name: ['Ya existe una empresa con ese nombre: «Altura Viva».'] } },
      }),
    )
    renderPage()
    await screen.findByDisplayValue('Andes Tours')

    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'altura viva' } })
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }))

    const message = await screen.findByText('Ya existe una empresa con ese nombre: «Altura Viva».')
    expect(message.id).toBe('name-error')
    await waitFor(() => expect(updateMyCompany).toHaveBeenCalledTimes(1))
  })
})
