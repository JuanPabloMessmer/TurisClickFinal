import { CatalogList } from '@/features/catalog/CatalogList'
import { useMyReservations } from '@/features/reservations/api'
import { TripCard, TripCardSkeleton } from '@/features/reservations/components'
import { formatCountdown, secondsLeft } from '@/features/reservations/model'
import { useNow } from '@/features/reservations/useCountdown'

/**
 * "Mis viajes": una sola lista paginada en el orden real del backend (creación descendente), con
 * pull-to-refresh y scroll infinito. Sin pestañas por estado: el endpoint no filtra, y filtrar solo las
 * páginas ya cargadas mostraría resultados incompletos. Sin polling: se revalida al entrar y al tirar.
 */
export function TripsList() {
  const query = useMyReservations()
  // Un único reloj para todas las tarjetas pendientes, en vez de un intervalo por tarjeta.
  const now = useNow(1000)

  return (
    <CatalogList
      query={query}
      emptyTitle="Todavía no tenés viajes"
      emptyMessage="Cuando reserves una experiencia o un paquete, va a aparecer acá."
      renderSkeleton={() => <TripCardSkeleton />}
      renderItem={(reservation) => (
        <TripCard
          reservation={reservation}
          now={now}
          countdownLabel={reservation.expiresAt ? formatCountdown(secondsLeft(reservation.expiresAt, now)) : null}
        />
      )}
    />
  )
}
