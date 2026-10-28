import {
  Building,
  Building2,
  ClipboardList,
  Compass,
  LayoutDashboard,
  MapPinned,
  Package,
  Plane,
  ShieldCheck,
  Tags,
  Users,
} from 'lucide-react'
import { PROVIDER_FLIGHTS_ENABLED } from '@/lib/features'

/**
 * Navegación agrupada por el trabajo, no por la entidad: primero dónde se empieza el día, después lo
 * que se vende y por último la configuración. Sin agrupar, seis links sueltos obligan a leerlos todos
 * cada vez (DESIGN.md §1).
 */
export const adminNav = [
  { label: null, links: [{ to: '/dashboard', label: 'Hoy', icon: LayoutDashboard }] },
  {
    label: 'Plataforma',
    links: [
      { to: '/admin/companies', label: 'Empresas', icon: Building2 },
      { to: '/admin/reservations', label: 'Reservas', icon: ClipboardList },
      { to: '/admin/users', label: 'Usuarios', icon: Users },
    ],
  },
  {
    label: 'Catálogo publicado',
    links: [
      { to: '/admin/experiences', label: 'Experiencias', icon: Compass },
      { to: '/admin/packages', label: 'Paquetes', icon: Package },
      ...(PROVIDER_FLIGHTS_ENABLED ? [{ to: '/admin/flights', label: 'Vuelos', icon: Plane }] : []),
    ],
  },
  {
    label: 'Configuración',
    links: [
      { to: '/admin/destinations', label: 'Destinos', icon: MapPinned },
      { to: '/admin/categories', label: 'Categorías', icon: Tags },
      { to: '/admin/administrators', label: 'Administradores', icon: ShieldCheck },
    ],
  },
]

export const providerNav = [
  { label: null, links: [{ to: '/dashboard', label: 'Hoy', icon: LayoutDashboard }] },
  {
    label: 'Lo que vendés',
    links: [
      { to: '/provider/experiences', label: 'Experiencias', icon: Compass },
      { to: '/provider/packages', label: 'Paquetes', icon: Package },
      ...(PROVIDER_FLIGHTS_ENABLED ? [{ to: '/provider/flights', label: 'Vuelos', icon: Plane }] : []),
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
