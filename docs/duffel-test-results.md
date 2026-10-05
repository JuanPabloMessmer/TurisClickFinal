# Duffel en modo de prueba — evidencia de la integración

<!-- Generado por tools/duffel-spike; no editar a mano. -->

Qué prueba este documento: que TurisClick habla con una API de inventario aéreo real —no con un
mock— y que el ciclo buscar → revalidar → reservar funciona de punta a punta.

**Garantías de esta corrida**, verificables en los datos de abajo:

- se usó exclusivamente el **modo de prueba** de Duffel (token `duffel_test_…`; el adapter se
  niega a arrancar con uno que no lo sea);
- toda oferta y toda orden volvió con `live_mode: false`;
- **no se movió dinero real**: en modo de prueba el saldo de la cuenta es ilimitado y el pago se
  declara contra ese balance, sin tarjeta;
- los pasajeros fueron **sintéticos**, sin un solo dato personal real;
- el token no aparece en este documento ni en ningún log.

Corrida: 2026-10-05 00:15 -04:00
Fecha de salida usada en las búsquedas: 2026-11-19 (ida y vuelta: 2026-11-26)

## Cobertura por ruta

| Ruta | Ofertas | Tiempo | Aerolínea | Precio más bajo | Resultado |
|---|---|---|---|---|---|
| `VVI-LPB` | 20 | 2704 ms | Iberia (IB) | USD 48.38 | ofertas disponibles |
| `VVI-CBB` | 20 | 756 ms | Iberia (IB) | USD 40.75 | ofertas disponibles |
| `LPB-VVI` | 20 | 707 ms | Duffel Airways (ZZ) | USD 49.01 | ofertas disponibles |
| `LPB-CBB` | 20 | 747 ms | Duffel Airways (ZZ) | USD 37.89 | ofertas disponibles |
| `CBB-VVI` | 20 | 694 ms | British Airways (BA) | USD 40.84 | ofertas disponibles |
| `LHR-JFK` | 20 | 1855 ms | American Airlines (AA) | USD 221.42 | ofertas disponibles |
| `JFK-LHR` | 20 | 2485 ms | Iberia (IB) | USD 220.82 | ofertas disponibles |
| `MAD-LIM` | 20 | 1109 ms | American Airlines (AA) | USD 351.60 | ofertas disponibles |
| `GRU-EZE` | 20 | 1886 ms | Duffel Airways (ZZ) | USD 89.88 | ofertas disponibles |

## Escenarios documentados por Duffel

| Ruta | Comportamiento esperado | Ofertas | Observado |
|---|---|---|---|
| `PVD-RAI` | sin ofertas | 0 | sin ofertas |
| `LHR-STN` | cambio de precio al revalidar | 1 | 1 oferta(s); al revalidar USD 32.30 → USD 42.30 |
| `LGW-LHR` | oferta vencida | 1 | 1 oferta(s); expires_at = 04:46:00 UTC |
| `LHR-DXB` | vuelo con escalas | 20 | 20 oferta(s) |

## Ciclo completo: buscar → revalidar → reservar → cancelar

Ruta elegida: **VVI → LPB** (la primera con inventario).

### 1. Oferta seleccionada

- id: `off_0000BB…jbto`
- aerolínea: Iberia (IB)
- precio: **USD 48.38**
- vence: 2026-10-05 04:45:47 UTC
- `live_mode`: **False**
- documentos de identidad requeridos: False
- pago inmediato requerido: False
- pasajeros esperados: 1
- segmento: VVI → LPB, 2026-11-19 13:56 → 15:15, IB3177, equipaje facturado: 1

### 2. Revalidación

- `GET /air/offers/{id}` respondió en 318 ms
- el precio se mantuvo en USD 48.38

### 3. Orden de prueba

Pasajero **sintético**; no se usó ningún dato personal real.

- `POST /air/orders` respondió en 485 ms
- id de orden: `ord_0000BB…3z9N`
- localizador: `TNQDQU`
- total: USD 48.38
- `live_mode`: **False** (false = la reserva vive sólo en el entorno de prueba)

### 4. Cancelación

- cancelación `ore_0000BB…5rnt` confirmada a las 04:16:03 UTC
- reintegro: USD 48.38 → balance



---

Generado el 2026-10-05 00:15 -04:00. Reproducible con `dotnet run --project tools/duffel-spike`
(requiere el token de prueba en User Secrets).
