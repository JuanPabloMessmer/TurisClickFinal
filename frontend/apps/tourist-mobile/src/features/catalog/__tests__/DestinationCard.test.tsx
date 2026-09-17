import { fireEvent, render, screen } from '@testing-library/react-native'
import { Image } from 'react-native'
import { DestinationBanner, DestinationCard } from '@/features/catalog/DestinationCard'

const sucre = {
  id: 'city-sucre',
  name: 'Sucre',
  type: 'CITY',
  publishedExperienceCount: 9,
  imageUrl: 'https://upload.wikimedia.org/wikipedia/commons/thumb/x/xx/Sucre.jpg/1280px-Sucre.jpg',
}

describe('DestinationCard', () => {
  it('muestra la foto del destino, su nombre y cuántas experiencias tiene', () => {
    const onPress = jest.fn()
    render(<DestinationCard destination={sucre} onPress={onPress} />)

    const card = screen.getByRole('button', { name: 'Explorar Sucre, 9 experiencias' })
    expect(screen.getByText('Sucre')).toBeTruthy()
    expect(screen.getByText('9 experiencias')).toBeTruthy()
    expect(screen.UNSAFE_getByType(Image).props.source).toEqual({ uri: sucre.imageUrl })

    fireEvent.press(card)
    expect(onPress).toHaveBeenCalled()
  })

  it('sin imagen usa el placeholder y conserva el nombre legible', () => {
    render(<DestinationCard destination={{ ...sucre, imageUrl: null, publishedExperienceCount: 0 }} onPress={jest.fn()} />)

    expect(screen.getByText('🏔️')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Explorar Sucre' })).toBeTruthy()
    expect(screen.queryByText(/experiencia/)).toBeNull()
  })

  it('si la imagen falla al cargar, cae al placeholder sin romper el layout', () => {
    render(<DestinationCard destination={sucre} onPress={jest.fn()} />)

    fireEvent(screen.UNSAFE_getByType(Image), 'error')

    expect(screen.getByText('🏔️')).toBeTruthy()
    expect(screen.getByText('Sucre')).toBeTruthy()
  })
})

describe('DestinationBanner', () => {
  it('muestra el destino filtrado y permite quitar el filtro', () => {
    const onClear = jest.fn()
    render(<DestinationBanner destination={sucre} onClear={onClear} />)

    expect(screen.getByText('Sucre')).toBeTruthy()
    fireEvent.press(screen.getByRole('button', { name: 'Quitar el filtro Sucre' }))
    expect(onClear).toHaveBeenCalled()
  })
})
