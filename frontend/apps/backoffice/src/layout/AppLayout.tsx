import {
  Building,
  Building2,
  ClipboardList,
  Compass,
  LayoutDashboard,
  LogOut,
  MapPinned,
  Menu,
  Package,
  Tags,
  X,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '@/auth/useAuth'
import { BrandMark } from '@/components/BrandMark'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

/**
 * Navegación agrupada por el trabajo, no por la entidad: primero dónde se empieza el día, después lo
 * que se vende y por último la configuración. Sin agrupar, seis links sueltos obligan a leerlos todos
 * cada vez (DESIGN.md §1).
 */
const adminNav = [
  { label: null, links: [{ to: '/dashboard', label: 'Hoy', icon: LayoutDashboard }] },
  {
    label: 'Moderación',
    links: [{ to: '/admin/companies', label: 'Empresas', icon: Building2 }],
  },
  {
    label: 'Catálogo',
    links: [
      { to: '/admin/destinations', label: 'Destinos', icon: MapPinned },
      { to: '/admin/categories', label: 'Categorías', icon: Tags },
    ],
  },
]

const providerNav = [
  { label: null, links: [{ to: '/dashboard', label: 'Hoy', icon: LayoutDashboard }] },
  {
    label: 'Lo que vendés',
    links: [
      { to: '/provider/experiences', label: 'Experiencias', icon: Compass },
      { to: '/provider/packages', label: 'Paquetes', icon: Package },
    ],
  },
  {
    label: 'Operación',
    links: [
      { to: '/provider/reservations', label: 'Reservas', icon: ClipboardList },
      { to: '/provider/company', label: 'Mi empresa', icon: Building },
    ],
  },
]

function initials(fullName: string | null | undefined) {
  if (!fullName) return '?'
  const parts = fullName.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const { user, logout } = useAuth()
  const groups = user?.role === 'ADMIN' ? adminNav : providerNav

  return (
    <div className="flex h-full flex-col bg-brand-900 text-white">
      <div className="flex h-16 items-center px-5">
        <BrandMark subtitle={user?.role === 'ADMIN' ? 'Administración' : 'Panel del operador'} />
      </div>

      <nav className="flex flex-1 flex-col gap-6 overflow-y-auto px-3 py-4" aria-label="Navegación principal">
        {groups.map((group, index) => (
          <div key={group.label ?? `group-${index}`} className="flex flex-col gap-1">
            {group.label && (
              <p className="px-3 pb-1 text-caption font-semibold text-white/60">{group.label}</p>
            )}
            {group.links.map((link) => {
              const Icon = link.icon
              return (
                <NavLink
                  key={link.to}
                  to={link.to}
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    cn(
                      'flex h-11 items-center gap-3 rounded-sm px-3 text-label font-medium text-white/75 transition-colors hover:bg-white/10 hover:text-white',
                      // El estado activo es una superficie llena, no un borde lateral: se lee de un
                      // vistazo y no depende de 2px de color.
                      isActive && 'bg-primary font-semibold text-white hover:bg-primary',
                    )
                  }
                >
                  <Icon className="h-5 w-5 shrink-0" aria-hidden="true" />
                  {link.label}
                </NavLink>
              )
            })}
          </div>
        ))}
      </nav>

      <div className="border-t border-white/10 p-3">
        <div className="flex items-center gap-2.5 px-2 py-2">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-white/10 text-caption font-semibold text-white">
            {initials(user?.fullName)}
          </div>
          <div className="min-w-0 leading-tight">
            <p className="truncate text-label font-medium text-white">{user?.fullName}</p>
            <p className="truncate text-caption text-white/60">
              {user?.role === 'ADMIN' ? 'Administrador' : 'Operador'}
            </p>
          </div>
        </div>
        <Button
          variant="ghost"
          size="sm"
          className="mt-1 w-full justify-start gap-2 text-white/75 hover:bg-white/10 hover:text-white"
          onClick={() => void logout()}
        >
          <LogOut className="h-4 w-4" aria-hidden="true" />
          Cerrar sesión
        </Button>
      </div>
    </div>
  )
}

export function AppLayout() {
  const [mobileOpen, setMobileOpen] = useState(false)
  const location = useLocation()

  // Cambiar de pantalla cierra el drawer: dejarlo abierto encima del contenido nuevo es desorientador.
  useEffect(() => setMobileOpen(false), [location.pathname])

  return (
    <div className="min-h-screen bg-background">
      <aside className="fixed inset-y-0 left-0 hidden w-64 md:block">
        <SidebarContent />
      </aside>

      <header className="sticky top-0 z-30 flex h-14 items-center gap-3 bg-brand-900 px-3 md:hidden">
        <button
          type="button"
          onClick={() => setMobileOpen(true)}
          aria-label="Abrir menú de navegación"
          aria-expanded={mobileOpen}
          className="flex h-11 w-11 items-center justify-center rounded-sm text-white/80 hover:bg-white/10 hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/60"
        >
          <Menu className="h-5 w-5" aria-hidden="true" />
        </button>
        <BrandMark />
      </header>

      {mobileOpen && (
        <div className="fixed inset-0 z-40 md:hidden">
          <div className="absolute inset-0 bg-brand-900/60" onClick={() => setMobileOpen(false)} aria-hidden="true" />
          <div className="absolute inset-y-0 left-0 w-72 shadow-lg">
            <SidebarContent onNavigate={() => setMobileOpen(false)} />
            <button
              type="button"
              onClick={() => setMobileOpen(false)}
              aria-label="Cerrar menú de navegación"
              className="absolute right-2 top-2 flex h-11 w-11 items-center justify-center rounded-sm text-white/80 hover:bg-white/10 hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/60"
            >
              <X className="h-5 w-5" aria-hidden="true" />
            </button>
          </div>
        </div>
      )}

      <main className="md:pl-64">
        <div className="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8 lg:py-10">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
