import { Archive, Calendar, Pencil, Plane, Plus, Radio } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableLoadingRow, TableRow } from '@/components/ui/table'
import { useArchiveFlight, useMyFlights, usePublishFlight } from './api'
import { DemoBadge } from './DemoBadge'
import { FlightFeatureState } from './FlightFeatureState'
import { flightErrorKind, flightErrorMessage } from './flightErrors'
import { cabinLabel, flightCode, flightStatusLabel, flightStatusVariant } from './labels'

/**
 * Vuelos que vende la empresa. Es un producto distinto de la oferta de aerolíneas del paquete: acá la empresa
 * carga sus propios asientos (cupo o charter), fija el precio y después del pago emite el pasaje ella misma.
 */
export function ProviderFlightsPage() {
  const flights = useMyFlights()
  const publish = usePublishFlight()
  const archive = useArchiveFlight()
  const [actionError, setActionError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const run = async (id: string, action: (id: string) => Promise<unknown>) => {
    if (busyId) return
    setActionError(null)
    setBusyId(id)
    try {
      await action(id)
    } catch (error) {
      setActionError(flightErrorMessage(error))
    } finally {
      setBusyId(null)
    }
  }

  const blocked = flights.isError && flightErrorKind(flights.error) !== 'other'

  return (
    <div>
      <PageHeader
        title="Vuelos"
        description="Vuelos que vende tu empresa con asientos propios: ruta, horarios, tarifas y cupo. Después de cada pago, emitís el pasaje vos y lo registrás acá."
        actions={
          <>
            <DemoBadge />
            {!blocked && (
              <Button asChild>
                <Link to="/provider/flights/new">
                  <Plus className="h-4 w-4" aria-hidden="true" />
                  Nuevo vuelo
                </Link>
              </Button>
            )}
          </>
        }
      />

      {actionError && (
        <Alert variant="destructive" className="mb-4">
          {actionError}
        </Alert>
      )}

      {blocked ? (
        <FlightFeatureState error={flights.error} />
      ) : flights.isError ? (
        <Alert variant="destructive">{flightErrorMessage(flights.error)}</Alert>
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Vuelo</TableHead>
                <TableHead>Ruta</TableHead>
                <TableHead>Cabina</TableHead>
                <TableHead>Próximas salidas</TableHead>
                <TableHead>Estado</TableHead>
                <TableHead className="text-right">Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {flights.isPending && <TableLoadingRow colSpan={6} />}
              {flights.isSuccess && flights.data.length === 0 && (
                <TableEmptyRow
                  colSpan={6}
                  icon={Plane}
                  title="Todavía no cargaste ningún vuelo"
                  description="Usá 'Nuevo vuelo' para cargar la ruta; después le agregás salidas con fecha, asientos y tarifas."
                />
              )}
              {flights.data?.map((flight) => {
                const id = flight.id!
                const busy = busyId === id
                return (
                  <TableRow key={id}>
                    <TableCell className="font-medium">
                      {flight.carrierName} {flightCode(flight.carrierIata, flight.flightNumber)}
                    </TableCell>
                    <TableCell>
                      <span className="font-medium">
                        {flight.originIata} → {flight.destinationIata}
                      </span>
                      <span className="block text-xs text-muted-foreground">
                        {flight.originLabel} → {flight.destinationLabel}
                      </span>
                    </TableCell>
                    <TableCell>{cabinLabel[flight.cabinClass ?? ''] ?? flight.cabinClass}</TableCell>
                    <TableCell>{flight.upcomingDepartures ?? 0}</TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        <Badge variant={flightStatusVariant[flight.status ?? ''] ?? 'neutral'}>
                          {flightStatusLabel[flight.status ?? ''] ?? flight.status}
                        </Badge>
                        {flight.suspended && <Badge variant="destructive">Suspendido por TurisClick</Badge>}
                      </div>
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex flex-wrap justify-end gap-2">
                        <Button variant="outline" size="sm" asChild>
                          <Link to={`/provider/flights/${id}/edit`}>
                            <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                            Editar
                          </Link>
                        </Button>
                        <Button variant="outline" size="sm" asChild>
                          <Link to={`/provider/flights/${id}/departures`}>
                            <Calendar className="h-3.5 w-3.5" aria-hidden="true" />
                            Salidas
                          </Link>
                        </Button>
                        {flight.status !== 'PUBLISHED' && flight.status !== 'ARCHIVED' && (
                          <Button
                            size="sm"
                            loading={busy}
                            disabled={!!busyId || flight.suspended}
                            onClick={() => void run(id, (x) => publish.mutateAsync(x))}
                          >
                            <Radio className="h-3.5 w-3.5" aria-hidden="true" />
                            Publicar
                          </Button>
                        )}
                        {flight.status !== 'ARCHIVED' && (
                          <Button
                            size="sm"
                            variant="outline"
                            loading={busy}
                            disabled={!!busyId}
                            onClick={() => void run(id, (x) => archive.mutateAsync(x))}
                          >
                            <Archive className="h-3.5 w-3.5" aria-hidden="true" />
                            Archivar
                          </Button>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </Card>
      )}

      {flights.isSuccess && flights.data.length > 0 && (
        <p className="mt-4 text-xs text-muted-foreground">
          Para publicar hace falta al menos una salida futura con asientos. Un vuelo suspendido por TurisClick no
          aparece en la búsqueda hasta que lo reactiven.
        </p>
      )}
    </div>
  )
}
