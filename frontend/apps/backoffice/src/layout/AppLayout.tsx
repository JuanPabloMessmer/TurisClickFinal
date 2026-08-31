import { Building, Building2, ClipboardList, Compass, LogOut, MapPinned, Menu, Package, Tags, X } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '@/auth/useAuth'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

const adminLinks = [
  { to: '/admin/destinations', label: 'Destinos', icon: MapPinned },
  { to: '/admin/categories', label: 'Categorías', icon: Tags },
  { to: '/admin/companies', label: 'Empresas', icon: Building2 },
]

const providerLinks = [
  { to: '/provider/company', label: 'Mi Empresa', icon: Building },
  { to: '/provider/experiences', label: 'Experiencias', icon: Compass },
  { to: '/provider/packages', label: 'Paquetes', icon: Package },
  { to: '/provider/reservations', label: 'Reservas', icon: ClipboardList },
]

function initials(fullName: string | null | undefined) {
  if (!fullName) return '?'
  const parts = fullName.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

function BrandMark() {
  return (
    <div className="flex items-center gap-2.5 px-2">
      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-secondary text-sm font-bold text-secondary-foreground">
        T
      </div>
      <div className="leading-tight">
        <p className="text-base font-bold tracking-tight text-primary-foreground">TurisClick</p>
        <p className="text-[11px] font-medium uppercase tracking-wider text-primary-foreground/60">Backoffice</p>
      </div>
    </div>
  )
}

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const { user, logout } = useAuth()
  const links = user?.role === 'ADMIN' ? adminLinks : providerLinks

  return (
    <div className="flex h-full flex-col bg-primary text-primary-foreground">
      <div className="flex h-16 items-center px-4">
        <BrandMark />
      </div>

      <nav className="flex flex-1 flex-col gap-1 overflow-y-auto px-3 py-4" aria-label="Navegación principal">
        {links.map((link) => {
          const Icon = link.icon
          return (
            <NavLink
              key={link.to}
              to={link.to}
              onClick={onNavigate}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-lg border-l-2 border-transparent px-3 py-2 text-sm font-medium text-primary-foreground/70 transition-colors hover:bg-white/10 hover:text-primary-foreground',
                  isActive && 'border-secondary bg-white/10 font-semibold text-primary-foreground',
                )
              }
            >
              <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
              {link.label}
            </NavLink>
          )
        })}
      </nav>

      <div className="border-t border-white/10 p-3">
        <div className="flex items-center gap-2.5 rounded-lg px-2 py-2">
          <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-white/10 text-xs font-semibold text-primary-foreground">
            {initials(user?.fullName)}
          </div>
          <div className="min-w-0 leading-tight">
            <p className="truncate text-sm font-medium text-primary-foreground">{user?.fullName}</p>
            <p className="truncate text-xs text-primary-foreground/60">{user?.role === 'ADMIN' ? 'Administrador' : 'Provider'}</p>
          </div>
        </div>
        <Button
          variant="ghost"
          size="sm"
          className="mt-1 w-full justify-start gap-2 text-primary-foreground/70 hover:bg-white/10 hover:text-primary-foreground"
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

  return (
    <div className="min-h-screen bg-background">
      {/* Desktop: sidebar fija en flujo normal */}
      <aside className="fixed inset-y-0 left-0 hidden w-64 md:block">
        <SidebarContent />
      </aside>

      {/* Mobile: barra superior + drawer off-canvas */}
      <header className="sticky top-0 z-30 flex h-14 items-center gap-3 border-b border-border bg-primary px-4 md:hidden">
        <button
          type="button"
          onClick={() => setMobileOpen(true)}
          aria-label="Abrir menú de navegación"
          className="rounded-md p-1.5 text-primary-foreground/80 hover:bg-white/10 hover:text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/50"
        >
          <Menu className="h-5 w-5" aria-hidden="true" />
        </button>
        <span className="text-sm font-bold text-primary-foreground">TurisClick</span>
      </header>

      {mobileOpen && (
        <div className="fixed inset-0 z-40 md:hidden">
          <div className="absolute inset-0 bg-foreground/50" onClick={() => setMobileOpen(false)} aria-hidden="true" />
          <div className="absolute inset-y-0 left-0 w-64 shadow-lg">
            <SidebarContent onNavigate={() => setMobileOpen(false)} />
            <button
              type="button"
              onClick={() => setMobileOpen(false)}
              aria-label="Cerrar menú de navegación"
              className="absolute right-3 top-4 rounded-md p-1.5 text-primary-foreground/80 hover:bg-white/10 hover:text-primary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/50"
            >
              <X className="h-5 w-5" aria-hidden="true" />
            </button>
          </div>
        </div>
      )}

      <main className="md:pl-64">
        <div className="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
