import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { adminApi, companiesApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { NewProviderAccountPage } from '@/modules/companies/NewProviderAccountPage'

/**
 * Alta de operador (R-01 a R-06, docs/auditoria-validaciones.md): los errores aparecen en español debajo de cada
 * campo antes de enviar, los del servidor se ubican en su campo, y un nombre sólo parecido se advierte sin
 * bloquear.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    adminApi: { ...actual.adminApi, createProviderAccount: vi.fn() },
    companiesApi: { ...actual.companiesApi, checkCompanyName: vi.fn() },
  }
})
const createProviderAccount = vi.mocked(adminApi.createProviderAccount)
const checkCompanyName = vi.mocked(companiesApi.checkCompanyName)

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NewProviderAccountPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const VALID = {
  companyName: 'Andes Tours',
  legalDocument: '1234567011',
  contactEmail: 'contacto@andes.bo',
  contactPhone: '',
  firstName: 'Camila',
  lastName: 'Aramayo',
  email: 'camila@andes.bo',
}

function fill(values: Partial<typeof VALID>) {
  const all = { ...VALID, ...values }
  fireEvent.change(screen.getByLabelText('Nombre', { selector: '#companyName' }), { target: { value: all.companyName } })
  fireEvent.change(screen.getByLabelText('NIT'), { target: { value: all.legalDocument } })
  fireEvent.change(screen.getByLabelText('Correo de contacto de la empresa'), { target: { value: all.contactEmail } })
  fireEvent.change(screen.getByLabelText('Teléfono de contacto (opcional)'), { target: { value: all.contactPhone } })
  fireEvent.change(screen.getByLabelText('Nombre', { selector: '#firstName' }), { target: { value: all.firstName } })
  fireEvent.change(screen.getByLabelText('Apellido'), { target: { value: all.lastName } })
  fireEvent.change(screen.getByLabelText('Correo con el que va a entrar'), { target: { value: all.email } })
}

const submit = () => fireEvent.click(screen.getByRole('button', { name: 'Dar de alta y generar credenciales' }))

const httpError = (status: number, data: unknown) =>
  new AxiosError('Request failed', 'ERR_BAD_REQUEST', undefined, undefined, {
    status,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data,
  })

describe('NewProviderAccountPage', () => {
  it('muestra cada error en español debajo de su campo y no llama al servidor', async () => {
    checkCompanyName.mockResolvedValue({ name: '', similar: [] })
    renderPage()
    const before = createProviderAccount.mock.calls.length

    fill({ companyName: '123', legalDocument: 'NIT-DEMO-1020304', contactPhone: '7ABC4567', contactEmail: 'con espacios@x.bo', firstName: 'J' })
    submit()

    expect(await screen.findByText('El nombre de la empresa debe incluir al menos dos letras.')).toBeTruthy()
    expect(screen.getByText('El NIT sólo puede tener números.')).toBeTruthy()
    expect(screen.getByText('El teléfono no puede tener letras.')).toBeTruthy()
    expect(screen.getByText('El correo de contacto no puede tener espacios.')).toBeTruthy()
    expect(screen.getByText('El nombre debe tener entre 2 y 100 caracteres.')).toBeTruthy()
    expect(screen.getByLabelText('NIT').getAttribute('aria-invalid')).toBe('true')
    expect(createProviderAccount.mock.calls.length).toBe(before)
  })

  it('un NIT o un correo duplicados que informa el servidor quedan debajo de su campo', async () => {
    checkCompanyName.mockResolvedValue({ name: '', similar: [] })
    createProviderAccount.mockRejectedValue(
      httpError(409, {
        title: 'Conflicto',
        detail: 'Ya existe una empresa con ese NIT.',
        errorCode: 'DUPLICATE',
        errors: { legalDocument: ['Ya existe una empresa con ese NIT.'] },
      }),
    )
    renderPage()

    fill({})
    submit()

    const message = await screen.findByText('Ya existe una empresa con ese NIT.')
    expect(message.id).toBe('legalDocument-error')
    // No se repite en el aviso general.
    expect(screen.getAllByText('Ya existe una empresa con ese NIT.')).toHaveLength(1)
  })

  it('un nombre sólo parecido se advierte pero deja dar de alta', async () => {
    checkCompanyName.mockResolvedValue({
      name: 'Andes Tours SRL',

      similar: [{ id: 'c1', name: 'Andes Tours S.R.L.' }],
    })
    createProviderAccount.mockResolvedValue({
      company: { id: 'c2', name: 'Andes Tours SRL', status: 'APPROVED' },
      userId: 'u1',
      email: 'camila@andes.bo',
      fullName: 'Camila Aramayo',
      temporaryPassword: 'ABCD-EFGH-JKMN-a1',
      mustChangePassword: true,
    })
    renderPage()

    const name = screen.getByLabelText('Nombre', { selector: '#companyName' })
    fireEvent.change(name, { target: { value: 'Andes Tours SRL' } })
    fireEvent.blur(name)

    expect(await screen.findByText(/nombre parecido: «Andes Tours S\.R\.L\.»/)).toBeTruthy()

    fill({ companyName: 'Andes Tours SRL' })
    submit()
    await waitFor(() => expect(createProviderAccount).toHaveBeenCalled())
  })

  it('un nombre igual a una empresa existente se marca al salir del campo', async () => {
    checkCompanyName.mockResolvedValue({ name: 'andes tours', exactMatch: { id: 'c1', name: 'Andes Tours' }, similar: [] })
    renderPage()

    const name = screen.getByLabelText('Nombre', { selector: '#companyName' })
    fireEvent.change(name, { target: { value: 'andes tours' } })
    fireEvent.blur(name)

    expect(await screen.findByText('Ya existe una empresa con ese nombre: «Andes Tours».')).toBeTruthy()
  })

  it('explica cómo se va a guardar el teléfono', async () => {
    checkCompanyName.mockResolvedValue({ name: '', similar: [] })
    renderPage()

    fireEvent.change(screen.getByLabelText('Teléfono de contacto (opcional)'), { target: { value: '7 123 4567' } })

    expect(await screen.findByText('Se guarda como +59171234567 (celular).')).toBeTruthy()
  })
})
