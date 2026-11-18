import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { categoriesApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { describe, expect, it, vi } from 'vitest'
import { CategoriesPage } from '@/modules/categories/CategoriesPage'

/**
 * Categorías (R-07 a R-09). Antes el formulario no tenía esquema: sólo deshabilitaba el botón con menos de dos
 * caracteres, sin decir por qué, y el borrado de una categoría en uso la quitaba en silencio de todo el catálogo.
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    categoriesApi: { ...actual.categoriesApi, listCategories: vi.fn(), createCategory: vi.fn(), deleteCategory: vi.fn() },
  }
})
const listCategories = vi.mocked(categoriesApi.listCategories)
const createCategory = vi.mocked(categoriesApi.createCategory)
const deleteCategory = vi.mocked(categoriesApi.deleteCategory)

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <CategoriesPage />
    </QueryClientProvider>,
  )
}

const httpError = (status: number, data: unknown) =>
  new AxiosError('Request failed', 'ERR_BAD_REQUEST', undefined, undefined, {
    status,
    statusText: '',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data,
  })

async function openCreate() {
  fireEvent.click(await screen.findByRole('button', { name: /Crear categoría/ }))
  return screen.findByRole('dialog')
}

describe('CategoriesPage', () => {
  it('explica por qué un nombre no sirve, antes de enviar', async () => {
    listCategories.mockResolvedValue([])
    renderPage()
    const dialog = await openCreate()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: '4x4' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }))

    expect(await within(dialog).findByText('El nombre de la categoría debe incluir al menos dos letras.')).toBeTruthy()
    expect(createCategory).not.toHaveBeenCalled()
  })

  it('acepta números cuando tienen sentido y envía el nombre normalizado', async () => {
    listCategories.mockResolvedValue([])
    createCategory.mockResolvedValue({ id: 'c1', name: 'Turismo 4x4' })
    renderPage()
    const dialog = await openCreate()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: '  Turismo   4x4 ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }))

    await waitFor(() =>
      expect(createCategory).toHaveBeenCalledWith(expect.anything(), { name: 'Turismo 4x4', description: undefined }),
    )
  })

  it('un duplicado que informa el servidor queda debajo del nombre', async () => {
    listCategories.mockResolvedValue([])
    createCategory.mockRejectedValue(
      httpError(409, { detail: 'Ya existe la categoría «Aventura».', errorCode: 'DUPLICATE', errors: { name: ['Ya existe la categoría «Aventura».'] } }),
    )
    renderPage()
    const dialog = await openCreate()

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'aventura' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }))

    const message = await within(dialog).findByText('Ya existe la categoría «Aventura».')
    expect(message.id).toBe('category-name-error')
  })

  it('una categoría en uso no se elimina y se explica por qué', async () => {
    listCategories.mockResolvedValue([{ id: 'c1', name: 'Aventura' }])
    const detail =
      'No se puede eliminar «Aventura» porque está asignada a 4 experiencias y 1 paquete, y 12 turistas la eligieron como interés. Quitala de esos elementos antes de eliminarla.'
    deleteCategory.mockRejectedValue(httpError(409, { detail, errorCode: 'CATEGORY_IN_USE' }))
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: /Eliminar/ }))

    expect(await screen.findByText(detail)).toBeTruthy()
    expect(screen.getByText('Aventura')).toBeTruthy()
  })
})
