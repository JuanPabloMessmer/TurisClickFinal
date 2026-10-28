import { describe, expect, it } from 'vitest'
import { adminNav, providerNav } from '@/layout/navigation'

/**
 * Oleada 19: los vuelos son un metabuscador y las agencias sólo administran paquetes. La navegación no ofrece
 * cargar vuelos ni moderarlos; empresas, paquetes y reservas siguen.
 */
const paths = (nav: typeof adminNav) => nav.flatMap((group) => group.links.map((link) => link.to))

describe('navegación del Backoffice', () => {
  it('no muestra la carga ni la moderación de vuelos', () => {
    expect(paths(adminNav)).not.toContain('/admin/flights')
    expect(paths(providerNav)).not.toContain('/provider/flights')
  })

  it('mantiene empresas, paquetes y reservas', () => {
    expect(paths(adminNav)).toEqual(expect.arrayContaining(['/admin/companies', '/admin/packages', '/admin/reservations']))
    expect(paths(providerNav)).toEqual(expect.arrayContaining(['/provider/packages', '/provider/reservations']))
  })
})
