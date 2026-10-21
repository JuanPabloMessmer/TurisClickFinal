import type { ReservationItemResponse } from '@turisclick/api-client'
import { flightCode } from '@/modules/provider-flights/labels'

/** Qué se reservó en esta línea: la experiencia, el paquete o el vuelo (ruta y número). */
export function itemTitle(item: ReservationItemResponse): string {
  if (item.productType === 'FLIGHT' && item.flight) {
    const f = item.flight
    return `Vuelo ${f.originIata} → ${f.destinationIata} · ${flightCode(f.carrierIata, f.flightNumber)}`.trim()
  }
  return item.experienceTitle ?? item.packageTitle ?? '—'
}

export const isFlightItem = (item: ReservationItemResponse) => item.productType === 'FLIGHT' && !!item.flight
