import { formatCurrency, formatDate } from '@turisclick/utils'
import { Building2, CalendarClock, ClipboardList, Compass, MapPinned, Package, Plane, Tags, TriangleAlert } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useAuth } from '@/auth/useAuth'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { CapacityMeter } from '@/components/ui/capacity-meter'
import { EmptyState } from '@/components/ui/empty-state'
import { Spinner } from '@/components/ui/spinner'
import { useMyExperiences } from '@/modules/experiences/api'
import { useMyCompany } from '@/modules/my-company/api'
import { useMyPackages } from '@/modules/packages/api'
import { useCompanyReservations } from '@/modules/provider-reservations/api'
import { useAdminOverview } from '@/modules/platform/api'
import {
  useCategoriesCount,
  useDestinationsCount,
  usePendingCompanies,
  useUpcomingDepartures,
} from './api'

/**
 * "Hoy": lo que hay que hacer, no todo lo que existe. Antes, ADMIN aterrizaba en una tabla de destinos
 * y PROVIDER en el formulario de su propia empresa; ninguno de los dos veía el trabajo pendiente.
 *
 * Todo lo que se muestra sale de endpoints que ya existían. No hay métricas inventadas ni gráficos
 * decorativos: cada número es un conteo real y lleva a la pantalla donde se actúa sobre él.
 */
export function DashboardPage() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'ADMIN'

  return (
    <div>
      <PageHeader
        title={`Hoy${user?.firstName ? `, ${user.firstName}` : ''}`}
        description={capitalize(
          new Intl.DateTimeFormat('es-BO', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date()),
        )}
      />
      {isAdmin ? <AdminToday /> : <ProviderToday />}
    </div>
  )
}

function capitalize(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1)
}

/** Bloque de conteo. Siempre es un enlace: un número que no lleva a ningún lado no sirve para trabajar. */
function CountTile({ icon: Icon, label, value, to }: { icon: LucideIcon; label: string; value?: number; to: string }) {
  return (
    <Link
      to={to}
      className="flex items-center gap-3 rounded-md border border-border bg-card px-4 py-3.5 transition-colors hover:border-border-control"
    >
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-sm bg-muted">
        <Icon className="h-4.5 w-4.5 text-ink-muted" aria-hidden="true" />
      </span>
      <span className="min-w-0">
        <span className="block tabular text-title font-semibold leading-none text-foreground">
          {value ?? '—'}
        </span>
        <span className="mt-1 block truncate text-label text-ink-muted">{label}</span>
      </span>
    </Link>
  )
}

function SectionCard({
  title,
  hint,
  action,
  children,
}: {
  title: string
  hint?: string
  action?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-4 border-b border-border px-5 py-4">
        <div>
          <h2 className="text-heading font-semibold text-foreground">{title}</h2>
          {hint && <p className="mt-0.5 text-label text-ink-muted">{hint}</p>}
        </div>
        {action}
      </div>
      {children}
    </Card>
  )
}

// ---------------------------------------------------------------- ADMIN

function AdminToday() {
  const pending = usePendingCompanies()
  const destinations = useDestinationsCount()
  const categories = useCategoriesCount()
  const overview = useAdminOverview()

  const pendingItems = pending.data?.items ?? []
  const pendingCount = pending.data?.totalCount ?? 0
  const summary = overview.data

  // Lo que pide acción va PRIMERO y sólo aparece si existe: una fila que siempre dice "0 pendientes" deja de
  // leerse a la semana.
  const attention = [
    summary?.cancellationsNeedingReview
      ? {
          label:
            summary.cancellationsNeedingReview === 1
              ? '1 cancelación esperando resolución'
              : `${summary.cancellationsNeedingReview} cancelaciones esperando resolución`,
          to: '/admin/reservations?attention=1',
        }
      : null,
    summary?.flightsAwaitingReconciliation
      ? {
          label:
            summary.flightsAwaitingReconciliation === 1
              ? '1 pasaje sin confirmar con la aerolínea'
              : `${summary.flightsAwaitingReconciliation} pasajes sin confirmar con la aerolínea`,
          to: '/admin/reservations?attention=1',
        }
      : null,
    summary?.providerAccountsPendingFirstLogin
      ? {
          label:
            summary.providerAccountsPendingFirstLogin === 1
              ? '1 operador que todavía no entró por primera vez'
              : `${summary.providerAccountsPendingFirstLogin} operadores que todavía no entraron por primera vez`,
          to: '/admin/companies',
        }
      : null,
  ].filter((item): item is { label: string; to: string } => item !== null)

  return (
    <div className="flex flex-col gap-6">
      {attention.length > 0 && (
        <Card className="border-warning-fg/30 bg-warning-bg p-5">
          <h2 className="flex items-center gap-2 text-heading font-semibold text-warning-fg">
            <TriangleAlert className="h-4 w-4" aria-hidden="true" />
            Esperando resolución
          </h2>
          <ul className="mt-2 flex flex-col gap-1">
            {attention.map((item) => (
              <li key={item.label}>
                <Link to={item.to} className="text-body text-warning-fg underline-offset-4 hover:underline">
                  {item.label}
                </Link>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <SectionCard
        title="Empresas esperando aprobación"
        hint="Mientras no se aprueben, su catálogo no es visible para ningún turista."
        action={
          pendingCount > 0 ? (
            <Button asChild size="sm">
              <Link to="/admin/companies?status=PENDING">Revisar {pendingCount}</Link>
            </Button>
          ) : undefined
        }
      >
        {pending.isPending ? (
          <div className="flex justify-center py-10">
            <Spinner />
          </div>
        ) : pendingItems.length === 0 ? (
          <EmptyState
            icon={Building2}
            title="No hay solicitudes esperando"
            description="Las empresas que das de alta quedan aprobadas directamente; acá sólo aparecen las que dejaste pendientes."
          />
        ) : (
          <ul className="divide-y divide-border">
            {pendingItems.map((company) => (
              <li key={company.id} className="flex items-center justify-between gap-4 px-5 py-3">
                <div className="min-w-0">
                  <p className="truncate text-body font-medium text-foreground">{company.name}</p>
                  <p className="truncate text-label text-ink-muted">
                    {company.legalDocument ? `NIT ${company.legalDocument}` : 'Sin NIT declarado'}
                    {company.contactEmail ? ` · ${company.contactEmail}` : ''}
                  </p>
                </div>
                <Button asChild variant="outline" size="sm">
                  <Link to={`/admin/companies/${company.id}`}>Revisar</Link>
                </Button>
              </li>
            ))}
          </ul>
        )}
      </SectionCard>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <CountTile
          icon={Building2}
          label="Empresas operando"
          value={summary?.companiesApproved}
          to="/admin/companies?status=APPROVED"
        />
        <CountTile
          icon={Compass}
          label="Experiencias publicadas"
          value={summary?.experiencesPublished}
          to="/admin/experiences?status=PUBLISHED"
        />
        <CountTile
          icon={Package}
          label="Paquetes publicados"
          value={summary?.packagesPublished}
          to="/admin/packages?status=PUBLISHED"
        />
        <CountTile
          icon={Plane}
          label="Paquetes con vuelo"
          value={summary?.packagesWithFlight}
          to="/admin/packages?flight=YES"
        />
      </div>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <CountTile
          icon={ClipboardList}
          label="Reservas de hoy"
          value={summary?.reservationsToday}
          to="/admin/reservations"
        />
        <CountTile
          icon={CalendarClock}
          label="Reservas de los últimos 7 días"
          value={summary?.reservationsLast7Days}
          to="/admin/reservations"
        />
        <CountTile
          icon={ClipboardList}
          label="Confirmadas"
          value={summary?.reservationsConfirmed}
          to="/admin/reservations?status=CONFIRMED"
        />
        <CountTile
          icon={ClipboardList}
          label="Esperando pago"
          value={summary?.reservationsPendingPayment}
          to="/admin/reservations?status=PENDING_PAYMENT"
        />
      </div>

      {(summary?.chargedByCurrency ?? []).length > 0 && (
        <SectionCard
          title="Cobrado y devuelto"
          hint="Del libro de pagos, por moneda. No se suman monedas distintas: no existe conversión en TurisClick."
        >
          <div className="grid gap-4 px-5 py-4 sm:grid-cols-2">
            {(summary?.chargedByCurrency ?? []).map((money) => {
              const refunded = (summary?.refundedByCurrency ?? []).find((r) => r.currency === money.currency)
              return (
                <div key={money.currency} className="rounded-sm border border-border p-3">
                  <p className="text-label font-medium text-foreground">{money.currency}</p>
                  <p className="mt-1 text-title font-semibold text-foreground tabular-nums">
                    {formatCurrency(money.amount ?? 0, money.currency ?? 'USD')}
                  </p>
                  <p className="text-label text-ink-muted">
                    Devuelto: {formatCurrency(refunded?.amount ?? 0, money.currency ?? 'USD')}
                  </p>
                </div>
              )
            })}
          </div>
        </SectionCard>
      )}

      <div className="grid gap-3 sm:grid-cols-2">
        <CountTile icon={MapPinned} label="Destinos" value={destinations.data?.length} to="/admin/destinations" />
        <CountTile icon={Tags} label="Categorías" value={categories.data?.length} to="/admin/categories" />
      </div>
    </div>
  )
}

// ---------------------------------------------------------------- PROVIDER

const companyStateCopy: Record<string, { tone: 'warning' | 'destructive' | 'info'; title: string; body: string }> = {
  PENDING: {
    tone: 'warning',
    title: 'Tu empresa está en revisión',
    body: 'Podés cargar experiencias y fechas desde ahora; se van a poder publicar en cuanto un administrador apruebe la empresa.',
  },
  REJECTED: {
    tone: 'destructive',
    title: 'Tu solicitud fue rechazada',
    body: 'Revisá los datos de tu empresa y volvé a enviarlos para que un administrador los evalúe de nuevo.',
  },
  SUSPENDED: {
    tone: 'destructive',
    title: 'Tu empresa está suspendida',
    body: 'Tu catálogo no se muestra en la app y no se pueden crear reservas nuevas mientras dure la suspensión.',
  },
}

function ProviderToday() {
  const company = useMyCompany()
  const experiences = useMyExperiences()
  const packages = useMyPackages()
  const reservations = useCompanyReservations()

  const experienceItems = experiences.data?.items ?? []
  const packageItems = packages.data?.items ?? []
  const reservationItems = reservations.data?.items ?? []

  const departures = useUpcomingDepartures(experienceItems)

  const published = experienceItems.filter((item) => item.status === 'PUBLISHED').length
  const drafts = experienceItems.filter((item) => item.status !== 'PUBLISHED').length
  const pendingPayment = reservationItems.filter((item) => item.reservationStatus === 'PENDING_PAYMENT').length

  const companyState = company.data?.status ? companyStateCopy[company.data.status] : undefined

  return (
    <div className="flex flex-col gap-6">
      {companyState && (
        <Alert variant={companyState.tone}>
          <div>
            <p className="font-semibold">{companyState.title}</p>
            <p className="mt-0.5">{companyState.body}</p>
          </div>
        </Alert>
      )}

      <SectionCard
        title="Próximas salidas"
        hint="Fechas abiertas de tus experiencias publicadas, con el cupo que llevás vendido."
      >
        {departures.isLoading ? (
          <div className="flex justify-center py-10">
            <Spinner />
          </div>
        ) : departures.departures.length === 0 ? (
          <EmptyState
            icon={CalendarClock}
            title="No tenés fechas abiertas por delante"
            description="Una experiencia sin fechas futuras con cupo no se puede reservar, aunque esté publicada."
          />
        ) : (
          <ul className="divide-y divide-border">
            {departures.departures.map((departure) => (
              <li
                key={departure.availabilityId}
                className="flex flex-col gap-2 px-5 py-3 sm:flex-row sm:items-center sm:justify-between"
              >
                <div className="min-w-0">
                  <p className="truncate text-body font-medium text-foreground">{departure.experienceTitle}</p>
                  <p className="tabular text-label text-ink-muted">
                    {formatDate(departure.date)}
                    {departure.startTime ? ` · ${departure.startTime.slice(0, 5)}` : ''}
                  </p>
                </div>
                <div className="flex items-center gap-4">
                  <CapacityMeter reserved={departure.reservedSlots} total={departure.totalSlots} />
                  <Button asChild variant="ghost" size="sm">
                    <Link to={`/provider/experiences/${departure.experienceId}/availability`}>Ver fechas</Link>
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}

        {departures.withoutFutureDates.length > 0 && (
          <div className="border-t border-border bg-warning-soft px-5 py-3 text-label text-warning-soft-foreground">
            <span className="font-semibold">
              {departures.withoutFutureDates.length}{' '}
              {departures.withoutFutureDates.length === 1
                ? 'experiencia publicada no tiene fechas futuras'
                : 'experiencias publicadas no tienen fechas futuras'}
              :
            </span>{' '}
            {departures.withoutFutureDates.slice(0, 3).join(' · ')}
            {departures.withoutFutureDates.length > 3 ? ' …' : ''}
          </div>
        )}
        {departures.notInspected > 0 && (
          <p className="border-t border-border px-5 py-2 text-caption text-ink-muted">
            Este resumen mira tus 20 experiencias publicadas más recientes. Las otras{' '}
            {departures.notInspected} están en Experiencias.
          </p>
        )}
      </SectionCard>

      <SectionCard
        title="Reservas recibidas"
        hint={
          pendingPayment > 0
            ? `${pendingPayment} ${pendingPayment === 1 ? 'está' : 'están'} esperando el pago del turista.`
            : 'Las últimas que entraron.'
        }
        action={
          reservationItems.length > 0 ? (
            <Button asChild variant="outline" size="sm">
              <Link to="/provider/reservations">Ver todas</Link>
            </Button>
          ) : undefined
        }
      >
        {reservations.isPending ? (
          <div className="flex justify-center py-10">
            <Spinner />
          </div>
        ) : reservationItems.length === 0 ? (
          <EmptyState
            icon={ClipboardList}
            title="Todavía no recibiste reservas"
            description="Cuando un turista reserve una de tus fechas, la vas a ver acá."
          />
        ) : (
          <ul className="divide-y divide-border">
            {reservationItems.slice(0, 5).map((item) => (
              <li key={item.id} className="flex items-center justify-between gap-4 px-5 py-3">
                <div className="min-w-0">
                  <p className="truncate text-body font-medium text-foreground">
                    {item.experienceTitle ?? item.packageTitle}
                  </p>
                  <p className="tabular text-label text-ink-muted">
                    {item.date ? formatDate(item.date) : 'Sin fecha'} · {item.travelers}{' '}
                    {item.travelers === 1 ? 'viajero' : 'viajeros'} ·{' '}
                    {formatCurrency(item.subtotal ?? 0, item.currency ?? 'BOB')}
                  </p>
                </div>
                <Badge variant={item.reservationStatus === 'PENDING_PAYMENT' ? 'warning' : 'success'}>
                  {item.reservationStatus === 'PENDING_PAYMENT' ? 'Esperando pago' : 'Confirmada'}
                </Badge>
              </li>
            ))}
          </ul>
        )}
      </SectionCard>

      <div className="grid gap-3 sm:grid-cols-3">
        <CountTile icon={Compass} label="Experiencias publicadas" value={published} to="/provider/experiences" />
        <CountTile icon={Compass} label="Experiencias en borrador" value={drafts} to="/provider/experiences" />
        <CountTile icon={Package} label="Paquetes" value={packageItems.length} to="/provider/packages" />
      </div>
    </div>
  )
}
