import type { UseInfiniteQueryResult } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react-native'
import { AxiosError } from 'axios'
import { Text } from 'react-native'
import { CatalogList, type PagedResponse } from '@/features/catalog/CatalogList'

/**
 * Los cuatro estados de la lista. El de error importa especialmente: no alcanza con no romperse, la
 * persona tiene que entender qué pasó y poder reintentar sin cerrar la app.
 */

type Item = { id?: string; title?: string }

function queryStub(
  overrides: Partial<UseInfiniteQueryResult<{ pages: PagedResponse<Item>[] }, Error>>,
): UseInfiniteQueryResult<{ pages: PagedResponse<Item>[] }, Error> {
  return {
    data: undefined,
    error: null,
    isError: false,
    isPending: false,
    isRefetching: false,
    isFetchingNextPage: false,
    hasNextPage: false,
    fetchNextPage: jest.fn(),
    refetch: jest.fn(),
    ...overrides,
  } as unknown as UseInfiniteQueryResult<{ pages: PagedResponse<Item>[] }, Error>
}

const renderList = (query: UseInfiniteQueryResult<{ pages: PagedResponse<Item>[] }, Error>) =>
  render(
    <CatalogList
      query={query}
      emptyTitle="Sin experiencias"
      emptyMessage="Probá con otro destino."
      renderItem={(item) => <Text>{item.title}</Text>}
    />,
  )

it('mientras carga muestra skeletons y ningún mensaje', () => {
  renderList(queryStub({ isPending: true }))

  expect(screen.queryByText('Sin experiencias')).toBeNull()
  expect(screen.queryByText('No pudimos cargar esto')).toBeNull()
})

it('sin conexión explica qué pasó en vez de quedarse cargando', () => {
  renderList(queryStub({ isError: true, error: new AxiosError('Network Error', 'ERR_NETWORK') }))

  expect(screen.getByText('No pudimos cargar esto')).toBeTruthy()
  expect(screen.getByText(/Revisá tu conexión/)).toBeTruthy()
})

it('el reintento vuelve a pedir los datos', () => {
  const refetch = jest.fn()
  renderList(queryStub({ isError: true, error: new Error('boom'), refetch }))

  fireEvent.press(screen.getByText('Reintentar'))

  expect(refetch).toHaveBeenCalled()
})

it('sin resultados dice por qué y no muestra una lista vacía', () => {
  renderList(queryStub({ data: { pages: [{ items: [], totalPages: 0 }] } }))

  expect(screen.getByText('Sin experiencias')).toBeTruthy()
  expect(screen.getByText('Probá con otro destino.')).toBeTruthy()
})

it('aplana las páginas en una sola lista', () => {
  renderList(
    queryStub({
      data: {
        pages: [
          { items: [{ id: 'a', title: 'Uno' }] },
          { items: [{ id: 'b', title: 'Dos' }] },
        ],
      },
    }),
  )

  expect(screen.getByText('Uno')).toBeTruthy()
  expect(screen.getByText('Dos')).toBeTruthy()
})

it('el error gana sobre cualquier dato viejo en cache', () => {
  renderList(
    queryStub({ isError: true, error: new Error('boom'), data: { pages: [{ items: [{ id: 'a', title: 'Viejo' }] }] } }),
  )

  expect(screen.queryByText('Viejo')).toBeNull()
  expect(screen.getByText('No pudimos cargar esto')).toBeTruthy()
})
