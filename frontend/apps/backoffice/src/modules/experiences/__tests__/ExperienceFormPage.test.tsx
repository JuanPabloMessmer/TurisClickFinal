import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { catalogApi, destinationsApi, experiencesApi } from '@turisclick/api-client'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { ExperienceFormPage } from '@/modules/experiences/ExperienceFormPage'

/**
 * UC-P-04/05 — categorías de la experiencia. Lo que se fija: que se listan las del catálogo público, que al
 * editar vuelven marcadas las que ya tenía y que lo elegido viaja en categoryIds (el asistente las usa para
 * cruzar preferencias del viajero).
 */
vi.mock('@turisclick/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@turisclick/api-client')>()
  return {
    ...actual,
    catalogApi: { ...actual.catalogApi, listCategories: vi.fn() },
    destinationsApi: { ...actual.destinationsApi, listPublicDestinations: vi.fn() },
    experiencesApi: {
      ...actual.experiencesApi,
      getMyExperienceById: vi.fn(),
      createExperience: vi.fn(),
      updateExperience: vi.fn(),
    },
  }
})

const CATEGORIES = [
  { id: 'cat-aventura', name: 'Aventura' },
  { id: 'cat-gastro', name: 'Gastronomía' },
  { id: 'cat-cultura', name: 'Cultura' },
]

const EXPERIENCE = {
  id: 'exp-1',
  title: 'Trekking al glaciar',
  description: 'Caminata guiada sobre el hielo con equipo incluido.',
  destinationId: 'city-1',
  price: 120,
  currency: 'USD',
  categories: [{ id: 'cat-aventura', name: 'Aventura' }],
  images: [],
}

type Experience = Awaited<ReturnType<typeof experiencesApi.getMyExperienceById>>
type Destinations = Awaited<ReturnType<typeof destinationsApi.listPublicDestinations>>

function setup(path: string) {
  vi.mocked(catalogApi.listCategories).mockResolvedValue(CATEGORIES)
  vi.mocked(destinationsApi.listPublicDestinations).mockResolvedValue([
    { id: 'city-1', name: 'El Calafate', parentName: 'Santa Cruz' },
  ] as Destinations)
  vi.mocked(experiencesApi.getMyExperienceById).mockResolvedValue(EXPERIENCE as Experience)
  vi.mocked(experiencesApi.updateExperience).mockResolvedValue(EXPERIENCE as Experience)

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/provider/experiences/new" element={<ExperienceFormPage />} />
          <Route path="/provider/experiences/:id/edit" element={<ExperienceFormPage />} />
          <Route path="/provider/experiences" element={<p>Listado de experiencias</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const chip = (name: string) => screen.getByRole('button', { name })

describe('ExperienceFormPage — categorías', () => {
  it('lista las categorías del catálogo y ya no muestra el aviso de "todavía no"', async () => {
    setup('/provider/experiences/new')

    expect(await screen.findByRole('button', { name: 'Aventura' })).toBeTruthy()
    expect(chip('Gastronomía').getAttribute('aria-pressed')).toBe('false')
    expect(chip('Cultura')).toBeTruthy()
    expect(screen.queryByText(/todavía no están disponibles/)).toBeNull()
  })

  it('al editar precarga las categorías que ya tenía la experiencia', async () => {
    setup('/provider/experiences/exp-1/edit')

    await screen.findByDisplayValue('Trekking al glaciar')
    await waitFor(() => expect(chip('Aventura').getAttribute('aria-pressed')).toBe('true'))
    expect(chip('Gastronomía').getAttribute('aria-pressed')).toBe('false')
  })

  it('envía las categorías elegidas al guardar', async () => {
    setup('/provider/experiences/exp-1/edit')
    await screen.findByDisplayValue('Trekking al glaciar')
    await waitFor(() => expect(chip('Aventura').getAttribute('aria-pressed')).toBe('true'))

    fireEvent.click(chip('Gastronomía'))
    fireEvent.click(chip('Aventura'))
    expect(chip('Gastronomía').getAttribute('aria-pressed')).toBe('true')
    expect(chip('Aventura').getAttribute('aria-pressed')).toBe('false')

    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }))

    await waitFor(() => expect(experiencesApi.updateExperience).toHaveBeenCalledTimes(1))
    const [, id, body] = vi.mocked(experiencesApi.updateExperience).mock.calls[0]
    expect(id).toBe('exp-1')
    expect(body.categoryIds).toEqual(['cat-gastro'])
    expect(await screen.findByText('Listado de experiencias')).toBeTruthy()
  })
})
