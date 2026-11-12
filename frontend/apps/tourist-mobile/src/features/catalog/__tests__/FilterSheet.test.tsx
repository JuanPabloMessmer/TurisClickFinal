import { fireEvent, render, screen } from '@testing-library/react-native'
import { FilterSheet } from '@/features/catalog/FilterSheet'
import { EMPTY_FILTERS } from '@/features/catalog/filters'

/**
 * El panel edita un borrador y solo devuelve los filtros al confirmar. Eso es lo que evita disparar una
 * búsqueda por cada toque, así que se prueba explícitamente.
 */

jest.mock('@/features/catalog/queries', () => ({
  useCities: () => ({ data: [{ id: 'd1', name: 'La Paz' }, { id: 'd2', name: 'Uyuni' }] }),
  useCategories: () => ({ data: [{ id: 'c1', name: 'Aventura' }, { id: 'c2', name: 'Cultura' }] }),
}))

const onApply = jest.fn()
const onClose = jest.fn()

async function open(props: Partial<React.ComponentProps<typeof FilterSheet>> = {}) {
  return await render(
    <FilterSheet
      visible
      tab="experiences"
      filters={EMPTY_FILTERS}
      onApply={onApply}
      onClose={onClose}
      {...props}
    />,
  )
}

beforeEach(() => jest.clearAllMocks())

it('ofrece destinos y categorías traídos del backend', async () => {
  await open()

  expect(screen.getByText('La Paz')).toBeTruthy()
  expect(screen.getByText('Aventura')).toBeTruthy()
})

it('no busca al tocar una opción: solo al confirmar', async () => {
  await open()

  await fireEvent.press(screen.getByText('La Paz'))
  expect(onApply).not.toHaveBeenCalled()

  await fireEvent.press(screen.getByText('Ver resultados'))
  expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ destinationId: 'd1' }))
})

it('deselecciona al volver a tocar la opción activa', async () => {
  await open()

  await fireEvent.press(screen.getByText('Uyuni'))
  await fireEvent.press(screen.getByText('Uyuni'))
  await fireEvent.press(screen.getByText('Ver resultados'))

  expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ destinationId: undefined }))
})

it('combina varios filtros en una sola confirmación', async () => {
  await open()

  await fireEvent.press(screen.getByText('La Paz'))
  await fireEvent.press(screen.getByText('Cultura'))
  await fireEvent.press(screen.getByText('Hasta 300'))
  await fireEvent.press(screen.getByText('Con cupo desde hoy'))
  await fireEvent.press(screen.getByText('Ver resultados'))

  expect(onApply).toHaveBeenCalledTimes(1)
  expect(onApply).toHaveBeenCalledWith({
    destinationId: 'd1',
    categoryId: 'c2',
    priceMax: 300,
    onlyWithAvailability: true,
  })
})

it('"Limpiar" vacía el borrador sin cerrar el panel', async () => {
  await open({ filters: { ...EMPTY_FILTERS, destinationId: 'd1', priceMax: 300 } })

  await fireEvent.press(screen.getByText('Limpiar'))
  await fireEvent.press(screen.getByText('Ver resultados'))

  expect(onApply).toHaveBeenCalledWith(EMPTY_FILTERS)
  expect(onClose).not.toHaveBeenCalled()
})

it('descarta los cambios al cerrar sin confirmar', async () => {
  await open()

  await fireEvent.press(screen.getByText('La Paz'))
  await fireEvent.press(screen.getByLabelText('Cerrar filtros'))

  expect(onClose).toHaveBeenCalled()
  expect(onApply).not.toHaveBeenCalled()
})

it('la duración solo aparece en paquetes, porque las experiencias no la aceptan', async () => {
  const { rerender } = await open({ tab: 'experiences' })
  expect(screen.queryByText('1 a 3 días')).toBeNull()

  await rerender(
    <FilterSheet visible tab="packages" filters={EMPTY_FILTERS} onApply={onApply} onClose={onClose} />,
  )
  expect(screen.getByText('1 a 3 días')).toBeTruthy()
})

it('la duración viaja como un rango, no como texto', async () => {
  await open({ tab: 'packages' })

  await fireEvent.press(screen.getByText('4 a 7 días'))
  await fireEvent.press(screen.getByText('Ver resultados'))

  expect(onApply).toHaveBeenCalledWith(expect.objectContaining({ durationDays: { min: 4, max: 7 } }))
})

it('aclara que el precio no convierte monedas', async () => {
  await open()

  expect(screen.getByText(/no se convierten/i)).toBeTruthy()
})
