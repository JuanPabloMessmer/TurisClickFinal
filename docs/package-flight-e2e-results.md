# Reserva de paquete + vuelo: evidencia de punta a punta (Duffel modo de prueba)

<!-- Generado por tools/package-flight-e2e; no editar a mano. -->

Qué prueba este documento, y que ningún test con el proveedor falso puede probar: que el itinerario,
los nombres de pasajero y el importe que TurisClick arma son aceptables para una API aérea real, y
que el localizador que termina en "Mis viajes" lo emitió esa API.

**Garantías de esta corrida:**

- se usó exclusivamente el **modo de prueba** de Duffel (token `duffel_test_…`; el adapter se niega
  a arrancar con uno que no lo sea);
- **no se movió dinero real**: en modo de prueba el saldo de la cuenta es ilimitado y el pago se
  declara contra ese balance, sin tarjeta. El cobro al turista es el simulado de TurisClick, que es
  otra cosa y no se mezcla con esto;
- el pasajero fue **sintético**, sin un solo dato personal real;
- corrió contra la base de **desarrollo local**, nunca contra Azure ni contra V1;
- la orden creada se **canceló** al terminar;
- el token no aparece en este documento ni en ningún log.

Corrida: 2026-10-05 19:55 -04:00
API: http://localhost:5288 · proveedor aéreo: Duffel (modo de prueba)

- OK: admin autenticado
- OK: destino disponible para el paquete
- OK: operador registrado — 201
- OK: empresa aprobada por el admin — 200
- OK: paquete creado — 201
- OK: salida del 19/11/2026 publicada — 201
- OK: paquete publicado
- OK: regla de vuelo VVI → LPB configurada — 200
- OK: turista registrado
- OK: Duffel devolvió ofertas para VVI → LPB — 200
## Cotización

- Aerolínea informada por el proveedor: **British Airways**. Es inventario de **prueba**: el itinerario y la tarifa son sintéticos y no corresponden a disponibilidad real de esa aerolínea en esta ruta.
- Vuelo: BA0105 · sale 2026-11-19T13:56:00 · llega 2026-11-19T15:15:00
- Precio del pasaje: 67.66 USD · paquete: 480 USD
- Total combinado: 547.66 USD

- OK: revalidación contra Duffel: UNCHANGED — 200
- OK: reserva creada con el vuelo pendiente — 201
- OK: el vuelo quedó en PENDING antes de pagar
- OK: reenviar la misma cotización devuelve la misma reserva
- OK: pago simulado aceptado y orden creada en Duffel — 200
- OK: la reserva quedó CONFIRMED
- OK: el vuelo quedó CONFIRMED (CONFIRMED)
- OK: Duffel devolvió un localizador (6MS3OF)
- OK: la respuesta no filtra identificadores del proveedor
- OK: "Mis viajes" muestra el vuelo con su localizador
- OK: el itinerario quedó congelado con la aerolínea y los horarios
- OK: el detalle no devuelve datos del pasajero (no se guardaron)
## Resultado del flujo

| Paso | Resultado |
|---|---|
| Cotizar VVI → LPB | 200 |
| Revalidar | UNCHANGED |
| Reservar (cupo + intención de vuelo) | 201 |
| Reenviar la misma cotización | misma reserva |
| Pagar y emitir | 200 |
| Estado final de la reserva | CONFIRMED |
| Estado final del vuelo | CONFIRMED |
| Localizador | 6MS3OF |

- OK: orden de prueba cancelada (reintegro informado: 67.66 USD)

**Resultado: todas las verificaciones pasaron.**
