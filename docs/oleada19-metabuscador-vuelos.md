# Oleada 19: vuelos como metabuscador

TurisClick tiene dos servicios separados:

| Servicio | Modelo | Dónde se compra |
|---|---|---|
| **Vuelos** | Metabuscador: busca, compara y redirige | En el sitio del vendedor, nunca en TurisClick |
| **Paquetes** | Marketplace: las agencias publican, TurisClick reserva y cobra (simulado) | En TurisClick, con el transporte aéreo incluido |

## Qué cambió

- **Búsqueda** (`POST /api/flights/search`): igual que antes (mundo → Bolivia, ida e ida y vuelta, pasajeros,
  cabina, sin sesión con el proveedor simulado). Cada oferta suma `redirect`:
  `{ available, url, sellerName, unavailableReason }`. Cambio aditivo en el contrato.
- **Redirección** (`FlightRedirectPolicy`): sólo con oferta real (`LiveMode`) del proveedor, enlace dado por el
  proveedor, `https`, sin usuario en la URL, puerto por defecto, host por nombre y **idéntico** a uno autorizado.
  `unavailableReason`: `DEMO` (simulada o sandbox), `NO_LINK`, `LINK_NOT_ALLOWED`. TurisClick no arma URLs.
- **Vuelos de operador (Oleada 18)**: apagados por configuración. El código, las tablas y la migración 0015
  quedan. Endpoints de proveedor y admin y la reserva con `flightDepartureIds`: **501 `FEATURE_NOT_AVAILABLE`**.
- **App**: "Ver oferta en {vendedor}" abre el navegador sólo con `redirect.available` y `https`. Las ofertas DEMO
  dicen "Oferta de demostración · no se puede comprar". Sin ruta de reserva de vuelos.
- **Backoffice**: sin "Vuelos" en la navegación, sin la tarjeta de vendedor de vuelos ni el panel de emisión
  (`src/lib/features.ts`).

## Configuración

| Clave | Default | Qué hace |
|---|---|---|
| `Flights:Provider` | `Fake` | Proveedor de búsqueda. Duffel sigue apagado. |
| `Flights:ProviderFlights:Enabled` | `false` | Prende el código de la Oleada 18 (sólo para volver atrás). |
| `Flights:RedirectHosts:{Proveedor}:{n}` | vacío | Hosts exactos a los que se puede redirigir, por proveedor. |

Con el proveedor simulado no hace falta nada: sus ofertas nunca redirigen. Ninguna migración nueva.

## Por qué 501 para lo apagado

RFC 9110 §15.6.2: el servidor no soporta la funcionalidad requerida. No es 404 (existe y se puede prender),
ni 403 (no depende de quién lo pide), ni 410 (no es permanente). Es además el contrato que el Backoffice y la
app ya interpretan desde la Oleada 18 (B1), y el handler global no lo registra como error. El filtro corre
después de la autorización (sin sesión sigue siendo 401) y antes de validar el cuerpo.

## Pendiente

- Integrar un proveedor de metabuscador con permiso contractual de redirección (y sus `RedirectHosts`).
  Duffel es una API de reserva y no da enlaces de compra.
- Registro de clics de redirección (necesita tabla nueva).
