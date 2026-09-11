import { coverImageUrl } from '@/features/catalog/images'

describe('coverImageUrl', () => {
  it('devuelve null cuando no hay imágenes', () => {
    expect(coverImageUrl(undefined)).toBeNull()
    expect(coverImageUrl(null)).toBeNull()
    expect(coverImageUrl([])).toBeNull()
  })

  it('prefiere la marcada como cover aunque no sea la primera', () => {
    const url = coverImageUrl([
      { url: 'a.jpg', sortOrder: 0, isCover: false },
      { url: 'b.jpg', sortOrder: 1, isCover: true },
    ])

    expect(url).toBe('b.jpg')
  })

  it('sin cover, cae a la primera por sortOrder y no al orden del array', () => {
    const url = coverImageUrl([
      { url: 'segunda.jpg', sortOrder: 5 },
      { url: 'primera.jpg', sortOrder: 1 },
    ])

    expect(url).toBe('primera.jpg')
  })

  it('ignora una cover sin url y sigue buscando', () => {
    const url = coverImageUrl([
      { url: null, isCover: true, sortOrder: 0 },
      { url: 'real.jpg', sortOrder: 1 },
    ])

    expect(url).toBe('real.jpg')
  })

  it('no muta el array recibido al ordenarlo', () => {
    const images = [
      { url: 'b.jpg', sortOrder: 2 },
      { url: 'a.jpg', sortOrder: 1 },
    ]

    coverImageUrl(images)

    expect(images[0].url).toBe('b.jpg')
  })
})
