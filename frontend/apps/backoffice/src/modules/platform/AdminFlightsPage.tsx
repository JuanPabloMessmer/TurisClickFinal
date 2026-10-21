import type { providerFlightsApi } from '@turisclick/api-client'
import { Plane } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { DemoBadge } from '@/modules/provider-flights/DemoBadge'
import { FlightFeatureState } from '@/modules/provider-flights/FlightFeatureState'
import { flightErrorKind, flightErrorMessage } from '@/modules/provider-flights/flightErrors'
import { cabinLabel, flightCode, flightStatusLabel, flightStatusVariant } from '@/modules/provider-flights/labels'
import { ConfirmActionDialog } from './ConfirmActionDialog'
import { useAdminFlights, useSuspendFlight, useUnsuspendFlight } from './flightsApi'

type Flight = providerFlightsApi.ProviderFlightResponse
const PAGE_SIZE = 20

/**
 * Moderación de los vuelos de operador. Suspender saca el vuelo de la búsqueda sin tocar lo ya vendido: las
 * reservas existentes siguen y el operador las sigue emitiendo.
 */
export function AdminFlightsPage() {
  const [status, setStatus] = useState('ALL')
  const [suspended, setSuspended] = useState('ALL')
  const [page, setPage] = useState(1)
  const flights = useAdminFlights({
    status: status === 'ALL' ? undefined : status,
    suspended: suspended === 'ALL' ? undefined : suspended === 'YES',
    page,
    pageSize: PAGE_SIZE,
  })
  const suspend = useSuspendFlight()
  const unsuspend = useUnsuspendFlight()
  const [target, setTarget] = useState<Flight | null>(null)
  const [error, setError] = useState<string | null>(null)

  const blocked = flights.isError && flightErrorKind(flights.error) !== 'other'
  const items = flights.data?.items ?? []
  const totalPages = Math.max(1, Math.ceil((flights.data?.totalCount ?? 0) / PAGE_SIZE))

  const onConfirm = async () => {
    if (!target) return
    setError(null)
    try {
      if (target.suspended) await unsuspend.mutateAsync(target.id!)
      else await suspend.mutateAsync(target.id!)
      setTarget(null)
    } catch (err) {
      setError(flightErrorMessage(err))
    }
  }

  return (
    <div>
      <PageHeader
        title="Vuelos"
        description="Los vuelos que publican las empresas autorizadas. Desde acá suspendés los que incumplen las políticas."
        actions={<DemoBadge />}
      />

      {blocked ? (
        <FlightFeatureState error={flights.error} />
      ) : (
        <>
          <div className="mb-4 flex flex-wrap gap-3">
            <Select value={status} onValueChange={(v) => { setStatus(v); setPage(1) }}>
              <SelectTrigger className="w-48" aria-label="Filtrar por estado">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="ALL">Todos los estados</SelectItem>
                <SelectItem value="PUBLISHED">Publicados</SelectItem>
                <SelectItem value="DRAFT">Borradores</SelectItem>
                <SelectItem value="ARCHIVED">Archivados</SelectItem>
              </SelectContent>
            </Select>
            <Select value={suspended} onValueChange={(v) => { setSuspended(v); setPage(1) }}>
              <SelectTrigger className="w-48" aria-label="Filtrar por suspensión">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="ALL">Suspendidos y no</SelectItem>
                <SelectItem value="YES">Sólo suspendidos</SelectItem>
                <SelectItem value="NO">Sin suspender</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {flights.isError && <Alert variant="destructive" className="mb-4">{flightErrorMessage(flights.error)}</Alert>}

          <Card>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Vuelo</TableHead>
                  <TableHead>Empresa</TableHead>
                  <TableHead>Ruta</TableHead>
                  <TableHead>Próximas salidas</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead className="text-right">Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {flights.isPending && <TableLoadingRow colSpan={6} />}
                {flights.isSuccess && items.length === 0 && (
                  <TableEmptyRow colSpan={6} icon={Plane} title="No hay vuelos con estos filtros" />
                )}
                {items.map((flight) => {
                  const name = `${flight.carrierName ?? ''} ${flightCode(flight.carrierIata, flight.flightNumber)}`.trim()
                  return (
                    <TableRow key={flight.id}>
                      <TableCell className="font-medium">
                        {name}
                        <span className="block text-xs text-muted-foreground">{cabinLabel[flight.cabinClass ?? ''] ?? flight.cabinClass}</span>
                      </TableCell>
                      <TableCell>{flight.companyName}</TableCell>
                      <TableCell>
                        {flight.originIata} → {flight.destinationIata}
                      </TableCell>
                      <TableCell>{flight.upcomingDepartures ?? 0}</TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          <Badge variant={flightStatusVariant[flight.status ?? ''] ?? 'neutral'}>
                            {flightStatusLabel[flight.status ?? ''] ?? flight.status}
                          </Badge>
                          {flight.suspended && <Badge variant="destructive">Suspendido</Badge>}
                        </div>
                      </TableCell>
                      <TableCell className="text-right">
                        <Button
                          size="sm"
                          variant={flight.suspended ? 'outline' : 'destructive'}
                          aria-label={`${flight.suspended ? 'Reactivar' : 'Suspender'} ${name}`}
                          onClick={() => {
                            setError(null)
                            setTarget(flight)
                          }}
                        >
                          {flight.suspended ? 'Reactivar' : 'Suspender'}
                        </Button>
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          </Card>

          {totalPages > 1 && (
            <div className="mt-4 flex items-center justify-end gap-2 text-sm">
              <span className="text-muted-foreground">
                Página {page} de {totalPages}
              </span>
              <Button variant="outline" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                Anterior
              </Button>
              <Button variant="outline" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                Siguiente
              </Button>
            </div>
          )}
        </>
      )}

      <ConfirmActionDialog
        open={!!target}
        title={target?.suspended ? '¿Reactivar este vuelo?' : '¿Suspender este vuelo?'}
        confirmLabel={target?.suspended ? 'Reactivar' : 'Suspender'}
        destructive={!target?.suspended}
        pending={suspend.isPending || unsuspend.isPending}
        error={error}
        onConfirm={() => void onConfirm()}
        onClose={() => setTarget(null)}
      >
        {target?.suspended ? (
          <p>Vuelve a aparecer en la búsqueda de los viajeros con las salidas que tenga abiertas.</p>
        ) : (
          <>
            <p>Deja de aparecer en la búsqueda y no admite reservas nuevas.</p>
            <p>Las reservas que ya existen no se cancelan: la empresa las sigue viendo y tiene que emitirlas.</p>
          </>
        )}
      </ConfirmActionDialog>
    </div>
  )
}
