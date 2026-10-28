import type { flightsApi } from '@turisclick/api-client'
import { Linking } from 'react-native'

type Option = flightsApi.FlightOptionResponse

/**
 * Qué se puede hacer con una oferta del metabuscador (Oleada 19). Los vuelos no se compran en TurisClick:
 * se abre el sitio del vendedor, y sólo cuando el backend lo validó (`redirect.available`, https y de un
 * dominio autorizado). La app vuelve a exigir https por si acaso; nunca arma ni completa una URL.
 *
 * - `open`: se puede ir al vendedor.
 * - `demo`: oferta de demostración. No es un precio real y no lleva a ningún lado.
 * - `unavailable`: sin enlace válido. Se muestra para comparar, pero no se puede comprar desde acá.
 */
export type OfferAction =
  | { kind: 'open'; url: string; seller: string }
  | { kind: 'demo' }
  | { kind: 'unavailable' }

export function offerActionOf(option: Option, demo: boolean): OfferAction {
  const redirect = option.redirect
  if (demo || redirect?.unavailableReason === 'DEMO') return { kind: 'demo' }
  if (!redirect?.available || !isSafeOfferUrl(redirect.url)) return { kind: 'unavailable' }
  return { kind: 'open', url: redirect.url, seller: sellerOf(option) }
}

export function sellerOf(option: Option): string {
  return option.redirect?.sellerName?.trim() || option.carrierName?.trim() || 'el vendedor'
}

/**
 * Segunda barrera, después de la del backend: https, con host, sin usuario en la URL, sin espacios ni barras
 * invertidas. Sin `new URL()`: en React Native sus getters no están garantizados sin un polyfill, y un fallo ahí
 * dejaría todas las ofertas como no disponibles.
 */
export function isSafeOfferUrl(url: string | null | undefined): url is string {
  if (!url || /[\s\\]/.test(url)) return false
  const authority = /^https:\/\/([^/?#]*)/i.exec(url)?.[1]
  return !!authority && !authority.includes('@')
}

/** Abre el sitio del vendedor en el navegador del teléfono. */
export async function openOffer(url: string): Promise<void> {
  if (!isSafeOfferUrl(url)) return
  await Linking.openURL(url)
}
