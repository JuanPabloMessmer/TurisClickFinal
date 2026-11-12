import { fireEvent, render, screen } from '@testing-library/react-native'
import { DestinationBanner, DestinationCard } from '@/features/catalog/DestinationCard'

const sucre = {
  id: 'city-sucre',
  name: 'Sucre',
  type: 'CITY',
  publishedExperienceCount: 9,
  imageUrl: 'https://upload.wikimedia.org/wikipedia/commons/thumb/x/xx/Sucre.jpg/1280px-Sucre.jpg',
}

describe('DestinationCard', () => {
  it('muestra la foto del destino, su nombre y cuántas experiencias tiene', async () => {
    const onPress = jest.fn()
    await render(<DestinationCard destination={sucre} onPress={onPress} />)

    const card = screen.getByRole('button', { name: 'Explorar Sucre, 9 experiencias' })
    expect(screen.getByText('Sucre')).toBeTruthy()
    expect(screen.getByText('9 experiencias')).toBeTruthy()
    expect(screen.getByTestId('catalog-image').props.source).toEqual([{ uri: sucre.imageUrl }])

    await fireEvent.press(card)
    expect(onPress).toHaveBeenCalled()
  })

  it('sin imagen usa el fallback tipográfico y conserva el nombre legible', async () => {
    await render(<DestinationCard destination={{ ...sucre, imageUrl: null, publishedExperienceCount: 0 }} onPress={jest.fn()} />)

    // El nombre aparece dos veces: como fallback de la foto y como rótulo de la tarjeta.
    expect(screen.getAllByText('Sucre').length).toBeGreaterThan(0)
    expect(screen.queryByTestId('catalog-image')).toBeNull()
    expect(screen.getByRole('button', { name: 'Explorar Sucre' })).toBeTruthy()
    expect(screen.queryByText(/experiencia/)).toBeNull()
  })

  it('si la imagen falla al cargar, cae al fallback sin romper el layout', async () => {
    await render(<DestinationCard destination={sucre} onPress={jest.fn()} />)

    await fireEvent(screen.getByTestId('catalog-image'), 'error', { nativeEvent: { error: 'no se pudo cargar' } })

    expect(screen.getAllByText('Sucre').length).toBeGreaterThan(0)
  })
})

describe('DestinationBanner', () => {
  it('muestra el destino filtrado y permite quitar el filtro', async () => {
    const onClear = jest.fn()
    await render(<DestinationBanner destination={sucre} onClear={onClear} />)

    expect(screen.getByText('Sucre')).toBeTruthy()
    await fireEvent.press(screen.getByRole('button', { name: 'Quitar el filtro Sucre' }))
    expect(onClear).toHaveBeenCalled()
  })
})
