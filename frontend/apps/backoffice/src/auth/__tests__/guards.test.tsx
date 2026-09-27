import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RequireRole } from '@/auth/RequireRole'
import { RootRedirect } from '@/auth/RootRedirect'
import { useAuth } from '@/auth/useAuth'

/**
 * El Backoffice es exclusivo de ADMIN y PROVIDER, y cada uno ve solamente lo suyo. Estos tests fijan esa
 * frontera en el cliente; el backend la vuelve a aplicar en cada request (403), así que nadie entra por
 * navegar a una URL a mano.
 */
vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))
const mockedUseAuth = vi.mocked(useAuth)

type AuthState = ReturnType<typeof useAuth>
const session = (overrides: Partial<AuthState>): AuthState =>
  ({ status: 'unauthenticated', user: null, login: vi.fn(), logout: vi.fn(), ...overrides }) as AuthState

function renderAt(initialPath: string, element: React.ReactElement) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/login" element={<p>Pantalla de login</p>} />
        <Route path="/admin/destinations" element={<p>Destinos (admin)</p>} />
        <Route path="/provider/company" element={<p>Mi empresa (provider)</p>} />
        <Route element={element}>
          <Route path="/protegida" element={<p>Contenido protegido</p>} />
        </Route>
        <Route path="/" element={element} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => mockedUseAuth.mockReturnValue(session({})))

describe('RequireRole', () => {
  it('mientras restaura la sesión no decide nada todavía', () => {
    mockedUseAuth.mockReturnValue(session({ status: 'loading' }))

    renderAt('/protegida', <RequireRole roles={['ADMIN']} />)

    expect(screen.queryByText('Contenido protegido')).toBeNull()
    expect(screen.queryByText('Pantalla de login')).toBeNull()
  })

  it('sin sesión manda a login', () => {
    renderAt('/protegida', <RequireRole roles={['ADMIN']} />)

    expect(screen.getByText('Pantalla de login')).toBeTruthy()
  })

  it('un PROVIDER no entra a una ruta de ADMIN', () => {
    mockedUseAuth.mockReturnValue(session({ status: 'authenticated', user: { id: 'p1', role: 'PROVIDER', fullName: 'Prov' } as AuthState['user'] }))

    renderAt('/protegida', <RequireRole roles={['ADMIN']} />)

    expect(screen.getByText('Pantalla de login')).toBeTruthy()
    expect(screen.queryByText('Contenido protegido')).toBeNull()
  })

  it('un TOURIST nunca entra al Backoffice', () => {
    mockedUseAuth.mockReturnValue(session({ status: 'authenticated', user: { id: 't1', role: 'TOURIST', fullName: 'Turi' } as AuthState['user'] }))

    renderAt('/protegida', <RequireRole roles={['ADMIN', 'PROVIDER']} />)

    expect(screen.getByText('Pantalla de login')).toBeTruthy()
  })

  it('con el rol correcto muestra el contenido', () => {
    mockedUseAuth.mockReturnValue(session({ status: 'authenticated', user: { id: 'a1', role: 'ADMIN', fullName: 'Admin' } as AuthState['user'] }))

    renderAt('/protegida', <RequireRole roles={['ADMIN']} />)

    expect(screen.getByText('Contenido protegido')).toBeTruthy()
  })
})

describe('RootRedirect', () => {
  it('lleva a cada rol a su pantalla inicial', () => {
    mockedUseAuth.mockReturnValue(session({ status: 'authenticated', user: { id: 'a1', role: 'ADMIN', fullName: 'Admin' } as AuthState['user'] }))
    renderAt('/', <RootRedirect />)
    expect(screen.getByText('Destinos (admin)')).toBeTruthy()

    mockedUseAuth.mockReturnValue(session({ status: 'authenticated', user: { id: 'p1', role: 'PROVIDER', fullName: 'Prov' } as AuthState['user'] }))
    renderAt('/', <RootRedirect />)
    expect(screen.getByText('Mi empresa (provider)')).toBeTruthy()
  })

  it('sin sesión va a login', () => {
    renderAt('/', <RootRedirect />)
    expect(screen.getByText('Pantalla de login')).toBeTruthy()
  })
})
