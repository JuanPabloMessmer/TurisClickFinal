# Reserva de paquete + vuelo: evidencia de punta a punta (Duffel modo de prueba)

<!-- Generado por tools/package-flight-e2e; no editar a mano. -->

Qué prueba este documento, y que ningún test con el proveedor falso puede probar: que el itinerario,
los nombres de pasajero y el importe que TurisClick arma son aceptables para una API aérea real, que
el localizador que termina en "Mis viajes" lo emitió esa API, y que el reembolso que se le muestra
a la persona al cancelar es el que esa API informó.

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

Corrida: 2026-10-06 22:17 -04:00
API: http://localhost:5288 · proveedor aéreo: Duffel (modo de prueba)

- OK: admin autenticado
- OK: destino disponible para el paquete
- OK: operador registrado — 201
- OK: empresa aprobada por el admin — 200
- OK: paquete creado — 201
- OK: salida del 21/11/2026 publicada — 201
- OK: paquete publicado
- OK: regla de vuelo VVI → LPB configurada — 200
- OK: turista registrado
- OK: Duffel devolvió ofertas para VVI → LPB — 200
## Cotización

- Aerolínea informada por el proveedor: **Iberia**. Es inventario de **prueba**: el itinerario y la tarifa son sintéticos y no corresponden a disponibilidad real de esa aerolínea en esta ruta.
- Vuelo: IB3167 · sale 2026-11-21T15:29:00 · llega 2026-11-21T16:48:00
- Precio del pasaje: 68.91 USD · paquete: 480 USD
- Total combinado: 548.91 USD

- OK: revalidación contra Duffel: UNCHANGED — 200
- OK: reserva creada con el vuelo pendiente — 201
- OK: el vuelo quedó en PENDING antes de pagar
- OK: reenviar la misma cotización devuelve la misma reserva
- OK: pago simulado aceptado y orden creada en Duffel — 200
- OK: la reserva quedó CONFIRMED
- OK: el vuelo quedó CONFIRMED (CONFIRMED)
- OK: Duffel devolvió un localizador (OJPKCI)
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
| Localizador | OJPKCI |

## Cancelación con reembolso

- OK: presupuesto de cancelación calculado — 200
- OK: el presupuesto tiene la línea del paquete
- OK: el presupuesto tiene la línea del vuelo, calculada aparte
| Componente | Pagado | Reembolso | Cargo | Según |
|---|---|---|---|---|
| Salar de Uyuni con vuelo 0f949f8d | 480.00 USD | 480.00 USD | 0.00 USD | política del operador (100%) |
| Vuelo VVI → LPB | 68.91 USD | 68.91 USD | 0.00 USD | lo que informó la aerolínea |

- OK: cancelación ejecutada — 200
- OK: la reserva quedó CANCELLED
- OK: la cancelación quedó COMPLETED (COMPLETED)
- OK: el pasaje quedó cancelado en la aerolínea
- OK: el cupo se liberó exactamente una vez (1 → 0)
- OK: el libro conserva el cobro y el reembolso (1 cobro(s), 2 reembolso(s))
| Movimiento | Importe | Componente | Estado |
|---|---|---|---|
| CHARGE | 548.91 USD | — | SUCCEEDED |
| REFUND | 68.91 USD | FLIGHT | SUCCEEDED |
| REFUND | 480.00 USD | PACKAGE | SUCCEEDED |

- Saldo USD: cobrado 548.91, devuelto 548.91, neto 0.00.

- OK: la reserva cancelada sigue en "Mis viajes"
- OK: se muestra como cancelada
- OK: con el resultado de la cancelación y lo reembolsado

**Resultado: todas las verificaciones pasaron.**
