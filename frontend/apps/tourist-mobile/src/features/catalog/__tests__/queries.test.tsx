import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react-native'
import {
  useCategories,
  useCities,
  useExperience,
  useExperienceAvailability,
  useExperiences,
  useInfiniteExperiences,
  useInfinitePackages,
  usePackage,
  usePackages,
} from '@/features/catalog/queries'

/**
 * Se mockea el transporte (httpClient) y no los wrappers de api-client, para que el test cubra también
 * las URLs y los query params que se mandan de verdad al backend público.
 */
const mockGet = jest.fn()

jest.mock('@/lib/httpClient', () => ({
  httpClient: { get: (...args: unknown[]) => mockGet(...args) },
}))

/**
 * Un QueryClient POR TEST, creado una sola vez: si se instancia dentro del componente wrapper, cada
 * re-render lo reemplaza y la cache se pierde — con lo que el scroll infinito nunca acumula páginas.
 */
const clients: QueryClient[] = []

function createWrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
}

beforeEach(() => {
  jest.clearAllMocks()
  mockGet.mockResolvedValue({ data: [] })
})

// Sin esto, los timers internos de la cache sobreviven al test y el worker de Jest no puede terminar.
afterEach(() => {
  for (const client of clients.splice(0)) client.clear()
})

describe('endpoints públicos del catálogo', () => {
  it('useCategories pega al endpoint público nuevo', async () => {
    mockGet.mockResolvedValue({ data: [{ id: 'c1', name: 'Aventura' }] })

    const { result } = renderHook(() => useCategories(), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mockGet).toHaveBeenCalledWith('/api/categories')
    expect(result.current.data).toEqual([{ id: 'c1', name: 'Aventura' }])
  })

  it('useCities pide solo destinos de tipo CITY, que es a lo que cuelgan los productos', async () => {
    const { result } = renderHook(() => useCities(), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mockGet).toHaveBeenCalledWith('/api/destinations', { params: { type: 'CITY' } })
  })

  it('useExperiences manda los filtros como query params', async () => {
    mockGet.mockResolvedValue({ data: { items: [], page: 1, pageSize: 20, totalCount: 0 } })

    const { result } = renderHook(
      () => useExperiences({ destinationId: 'd1', categoryId: 'c1', pageSize: 20 }),
      { wrapper: createWrapper() },
    )

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mockGet).toHaveBeenCalledWith('/api/experiences', {
      params: { destinationId: 'd1', categoryId: 'c1', pageSize: 20 },
    })
  })

  it('usePackages usa su propio endpoint, no el de experiencias', async () => {
    mockGet.mockResolvedValue({ data: { items: [] } })

    const { result } = renderHook(() => usePackages({ destinationId: 'd1' }), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mockGet).toHaveBeenCalledWith('/api/packages', { params: { destinationId: 'd1' } })
  })

  it('useExperience y su disponibilidad son dos requests distintas', async () => {
    mockGet.mockResolvedValue({ data: {} })

    const detail = renderHook(() => useExperience('e1'), { wrapper: createWrapper() })
    const availability = renderHook(() => useExperienceAvailability('e1'), { wrapper: createWrapper() })

    await waitFor(() => expect(detail.result.current.isSuccess).toBe(true))
    await waitFor(() => expect(availability.result.current.isSuccess).toBe(true))

    expect(mockGet).toHaveBeenCalledWith('/api/experiences/e1')
    expect(mockGet).toHaveBeenCalledWith('/api/experiences/e1/availability')
  })

  it('no dispara la request si todavía no hay id en la ruta', async () => {
    const { result } = renderHook(() => usePackage(''), { wrapper: createWrapper() })

    expect(result.current.fetchStatus).toBe('idle')
    expect(mockGet).not.toHaveBeenCalled()
  })

  it('deja el error disponible para que la pantalla lo muestre', async () => {
    mockGet.mockRejectedValue(new Error('500'))

    const { result } = renderHook(() => useExperiences({}), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(result.current.error).toBeInstanceOf(Error)
  })
})

describe('scroll infinito', () => {
  const page = (n: number, totalPages: number) => ({
    data: { items: [{ id: `e${n}` }], page: n, pageSize: 10, totalCount: totalPages * 10, totalPages },
  })

  it('pide la página siguiente y acumula los resultados', async () => {
    mockGet.mockResolvedValueOnce(page(1, 3)).mockResolvedValueOnce(page(2, 3))

    const { result, rerender } = renderHook(() => useInfiniteExperiences({ pageSize: 10 }), {
      wrapper: createWrapper(),
    })
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.hasNextPage).toBe(true)

    await act(async () => {
      await result.current.fetchNextPage()
    })
    // Sin un render nuevo, `result.current` sigue siendo el snapshot previo a la segunda página.
    rerender(undefined)

    expect(mockGet).toHaveBeenLastCalledWith('/api/experiences', { params: { pageSize: 10, page: 2 } })
    expect(result.current.data?.pages.flatMap((p) => p.items ?? [])).toHaveLength(2)
  })

  it('sabe que no hay más cuando la página actual es la última', async () => {
    mockGet.mockResolvedValue(page(1, 1))

    const { result } = renderHook(() => useInfiniteExperiences({ pageSize: 10 }), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.hasNextPage).toBe(false)
  })

  it('no pagina cuando no hay ningún resultado', async () => {
    mockGet.mockResolvedValue({ data: { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 } })

    const { result } = renderHook(() => useInfinitePackages({ pageSize: 10 }), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.hasNextPage).toBe(false)
  })

  it('los paquetes paginan contra su propio endpoint', async () => {
    mockGet.mockResolvedValue(page(1, 2))

    const { result } = renderHook(() => useInfinitePackages({ destinationId: 'd1', pageSize: 10 }), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mockGet).toHaveBeenCalledWith('/api/packages', {
      params: { destinationId: 'd1', pageSize: 10, page: 1 },
    })
  })
})
