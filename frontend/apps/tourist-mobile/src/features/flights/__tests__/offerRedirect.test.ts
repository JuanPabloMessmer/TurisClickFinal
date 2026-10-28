import { isSafeOfferUrl, offerActionOf } from '@/features/flights/search/redirect'

/** La segunda barrera de la app: aunque el backend se equivoque, no se abre nada inseguro ni nada DEMO. */
describe('redirección de ofertas', () => {
  it.each([
    'https://www.aerolinea-demo.com/compra?o=1',
    'HTTPS://www.aerolinea-demo.com',
    'https://reservas.agencia-demo.bo:443/x#y',
  ])('acepta %s', (url) => expect(isSafeOfferUrl(url)).toBe(true))

  it.each([
    null,
    '',
    'http://www.aerolinea-demo.com/',
    'javascript:alert(1)',
    'https://',
    'https://user@www.aerolinea-demo.com/',
    'https://www.aerolinea-demo.com@atacante.net/',
    'https://www.aerolinea-demo.com\\@atacante.net/',
    'https://www.aerolinea-demo.com/ compra',
    '//www.aerolinea-demo.com/',
  ])('rechaza %s', (url) => expect(isSafeOfferUrl(url)).toBe(false))

  it('una oferta DEMO nunca se abre, aunque traiga un enlace', () => {
    const option = { redirect: { available: true, url: 'https://www.aerolinea-demo.com/', sellerName: 'X', unavailableReason: null } }
    expect(offerActionOf(option, true)).toEqual({ kind: 'demo' })
    expect(offerActionOf({ redirect: { ...option.redirect, available: false, unavailableReason: 'DEMO' } }, false)).toEqual({ kind: 'demo' })
  })

  it('sin redirect del backend no hay compra', () => {
    expect(offerActionOf({ carrierName: 'Fake Airways' }, false)).toEqual({ kind: 'unavailable' })
  })
})
