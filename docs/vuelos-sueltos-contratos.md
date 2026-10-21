# Vuelos sueltos — contratos y dependencias (Workstreams B, C y D)

Estado: **B1 (contratos) y B2 (backend + migración 0015) implementados** en
`claude/flights-search-workstream-a`. Sin desplegar: la migración 0015 sobre `turisclick_db_v2` requiere
autorización. C (Backoffice) y A2 (app) se construyen contra estos contratos.

### Comportamiento de B2 que las pantallas tienen que conocer

- **Un ítem por tramo.** Ida y vuelta son dos `ReservationItem` FLIGHT, cada uno con su lista de pasajeros y
  su propia emisión. `travelers` = adultos + niños (los infantes van en brazos y no toman asiento).
- **Emisión:** `NOT_REQUIRED` mientras el ítem no está pagado → `PENDING` al pagar → `ISSUED` cuando el
  operador registra un número de pasaje **por pasajero, infantes incluidos**, en el orden de
  `item.flight.passengers` → o `FAILED` (ítem cancelado, reembolso total simulado, asientos liberados; si era
  lo último activo, la reserva queda cancelada).
- **Pago:** sólo se cobran y confirman los ítems pendientes de pago (arreglo P0 de la release); un tramo que
  el operador canceló antes del pago no se cobra ni revive.
- **Cancelación del turista con el pasaje sin emitir:** se devuelve el 100% aunque la política diga otra cosa.
  Con el pasaje emitido aplica la política congelada del vuelo. Componente de reembolso: `PROVIDER_FLIGHT`.
- **Lo vendido no cambia:** con reservas, la ruta, aerolínea, número, cabina y equipaje de un vuelo, y los
  horarios y tarifas de una salida, devuelven 409; los asientos no bajan de lo vendido; una salida con reservas
  no se borra (se cierra).
- **Búsqueda:** los vuelos de operador aparecen con `source: PROVIDER`, `bookable: true` y
  `providerFlight.departureIds`; sólo publicados, no suspendidos, de empresas aprobadas y autorizadas, en la
  cabina pedida y con asientos para todos. Ida y vuelta = pares del mismo operador y la misma moneda. La duración
  no se informa (los horarios son locales y no hay husos).
- **Empresa:** `CompanyResponse` trae `canSellFlights`, `flightSellerRegistration` y
  `flightSellerAuthorizedAt`. Sin autorización, el catálogo del operador responde 403
  `FLIGHT_SELLER_NOT_AUTHORIZED` (la emisión de lo vendido sigue funcionando).
- **Admin:** `GET /api/admin/reservations?productType=FLIGHT`; cada fila trae `flightIssuancePending` y
  `flightIssuanceOverdue` (pagado sin emitir hace más de 24 h, que también entra en `needsAttention`).
- **DEMO:** `ProviderFlightResponse.demo` y `ReservationItemResponse.flight.demo` son `true` en este entorno.

Decisiones del usuario: búsqueda sin sesión (con el proveedor simulado; la sesión se pide al reservar);
pasajeros con nombre y fecha de nacimiento **sintéticos**, sin documento; sólo ida e ida y vuelta; Duffel
apagado; "Pasaje emitido" sólo cuando el operador registra la emisión, y DEMO siempre en el entorno de prueba.

En código:
- Tipos: `src/TurisClick.Api/Modules/Flights/ProviderFlights/Dtos/ProviderFlightDtos.cs`.
- Endpoints: `src/TurisClick.Api/Modules/Flights/ProviderFlights/Controllers/ProviderFlightsController.cs`
  (operador y admin), y en `POST /api/reservations` la forma `flightDepartureIds` + `passengers`.
- Cliente: `frontend/packages/api-client/src/modules/providerFlights.ts` (`providerFlightsApi`), más los
  campos nuevos de `flightsApi` (`source`, `bookable`, `providerFlight`) y de `ReservationItemResponse.flight`.
- Servicios: `Modules/Flights/ProviderFlights/Services/` (operador y admin); reserva y pago en
  `ReservationService`; liberación de asientos en `ReservationBookingService.ReleaseHoldsAsync`.
- Migración: `20261021012403_0015_Oleada18_VuelosDeOperador`.
- Tests: `ProviderFlightContractTests.cs` (permisos y validación) y `ProviderFlightBookingTests.cs` (flujos).

Objetivo de producto: además del paquete con vuelo, TurisClick vende **vuelos sueltos publicados por
operadores autorizados**. El turista busca, elige, reserva y paga (pago simulado en la tesis); después del
pago, **el operador emite el pasaje fuera de TurisClick y registra la emisión**. Hasta que la emisión esté
registrada, nada en la app dice "pasaje emitido".

## 1. Dos fuentes de vuelos, y por qué no se mezclan

| | Vuelo de operador (nuevo) | Oferta de aerolínea (existente) |
|---|---|---|
| Quién vende y responde | La empresa operadora autorizada; su nombre se muestra en cada pantalla | TurisClick vía proveedor aéreo (Fake/Duffel) |
| Inventario | Asientos que el operador carga (cupo propio o charter) | Del proveedor, vigente ~20 minutos |
| Precio | Lo fija el operador; se congela al reservar (igual que experiencias) | Se revalida antes de cobrar |
| Disponibilidad | `UPDATE` condicional sobre asientos — el mismo patrón anti-sobreventa que ya existe | Del proveedor |
| Emisión | El operador emite fuera de TurisClick y registra número de pasaje / localizador | El proveedor, sólo en modo live (hoy desactivado) |
| Modelo | **`ReservationItem` con `CompanyId`** — conserva el aislamiento por empresa | `FlightBooking` 1–1 con `Reservation` |

Un vuelo de operador **sí tiene** empresa y cupo, que es exactamente lo que le falta a una oferta de
aerolínea y por lo que aquella vive aparte. Por eso entra como un ítem más y reutiliza reservas, pago
simulado, expiración, cancelación por política y el listado de reservas del operador.

## 2. Base de datos — migración `0015`, dueño único: Workstream B

**Una sola sesión crea y modifica migraciones.** Dos ramas que generan migraciones en paralelo chocan en
`TurisClickDbContextModelSnapshot.cs` y en el orden de aplicación; C y D no tocan entidades ni migraciones.
Todo es aditivo salvo el reemplazo de una restricción `CHECK`, que se hace en la misma migración.

| Cambio | Tipo |
|---|---|
| `companies.can_sell_flights boolean not null default false` | columna nueva |
| `companies.flight_seller_registration varchar(100) null` (registro/licencia declarada) | columna nueva |
| `companies.flight_seller_authorized_at timestamptz null`, `flight_seller_authorized_by uuid null` | columnas nuevas |
| `provider_flights` | tabla nueva |
| `provider_flight_departures` | tabla nueva |
| `reservation_items.provider_flight_departure_id uuid null` (FK) | columna nueva |
| `reservation_items.issuance_status varchar(20) not null default 'NOT_REQUIRED'` | columna nueva |
| `reservation_items.ticket_numbers varchar(500) null`, `issued_at`, `issued_by`, `issuance_note` | columnas nuevas |
| `reservation_passengers` | tabla nueva |
| `ProductType.FLIGHT` (se guarda como texto: no hay tipo enum que migrar) | valor nuevo |
| `ck_reservation_items_product_shape` | se reemplaza para admitir exactamente una de las tres availability |

```
provider_flights
  id, company_id (FK, índice), origin_iata char(3), destination_iata char(3),
  carrier_name varchar(100), carrier_iata char(2) null, flight_number varchar(10),
  cabin_class varchar(20), checked_bags int, carry_on_bags int,
  cancellation_policy (mismo formato que reservation_items), fare_rules text null,
  status (DRAFT | PUBLISHED | ARCHIVED), suspended_at null, created_at, updated_at
  CHECK origin_iata <> destination_iata

provider_flight_departures
  id, provider_flight_id (FK), departure_at timestamp (hora local, sin huso), arrival_at timestamp,
  total_seats int, available_seats int, adult_price numeric(12,2), child_price numeric(12,2),
  infant_price numeric(12,2), currency char(3), status (OPEN | CLOSED | CANCELLED), created_at, updated_at
  CHECK 0 <= available_seats <= total_seats; CHECK arrival_at > departure_at
  índice (provider_flight_id, departure_at)

reservation_passengers
  id, reservation_item_id (FK, cascade), type (ADULT | CHILD | INFANT),
  given_name, family_name, born_on date, created_at
```

Reglas que el backend valida (no sólo la app): destino en Bolivia (la misma regla del buscador), origen
conocido en el dataset de aeropuertos, fechas futuras, precios ≥ 0, moneda ISO, y que sólo una empresa con
`can_sell_flights` publique. Los infantes no ocupan asiento: `available_seats` descuenta adultos + niños.

Datos personales: los pasajeros se guardan porque el operador los necesita para emitir (a diferencia del
vuelo de proveedor, donde se envían y se descartan). Mínimo: nombre, apellido, fecha de nacimiento. Sin
documento de identidad en esta versión. En la demo, sólo datos sintéticos.

## 3. Contratos de API

### Turista

| Método | Ruta | Cambio |
|---|---|---|
| `POST` | `/api/flights/search` | Cada opción suma `source` (`PROVIDER` \| `AIRLINE`), `bookable`, y para `PROVIDER`: `departureIds` (1 o 2), `operatorName`, `seatsLeft`, `cancellationPolicy`. `bookable` del nivel superior pasa a "alguna opción es reservable". Se buscan vuelos de operador publicados con la ruta, la fecha y asientos suficientes; ida y vuelta = pares ida/vuelta |
| `POST` | `/api/reservations` | Nueva forma, excluyente con experiencia/paquete: `{ flightDepartureIds: [ida, vuelta?], passengers: [{ type, givenName, familyName, bornOn }] }`. Una transacción: toma asientos con `UPDATE` condicional por cada salida y congela precios |
| `POST` | `/api/reservations/{id}/pay` | Sin cambio de forma. Al confirmar, los ítems de vuelo quedan con `issuanceStatus = PENDING` |
| `GET` | `/api/reservations/{id}`, `/me` | Cada ítem de vuelo trae `flight`: ruta, horarios, aerolínea, número de vuelo, operador, `issuanceStatus`, `ticketNumbers`, pasajeros |
| `POST` | `/api/reservations/{id}/cancellation-quote`, `/cancel` | Sin cambio de forma: se aplica la política del operador por ítem. Con emisión `PENDING`, el reembolso es total |

### Operador (`RequireProvider` + empresa con `can_sell_flights`)

| Método | Ruta | Para qué |
|---|---|---|
| `GET` / `POST` | `/api/companies/me/flights` | Listar / crear (nace en `DRAFT`) |
| `GET` / `PUT` | `/api/companies/me/flights/{id}` | Ver / editar |
| `POST` | `/api/companies/me/flights/{id}/publish` · `/archive` | Publicar (exige al menos una salida futura con cupo) / archivar |
| `GET` / `POST` | `/api/companies/me/flights/{id}/departures` | Salidas: fecha y hora, asientos, tarifas |
| `PUT` / `DELETE` | `/api/companies/me/flight-departures/{id}` | Editar; borrar sólo sin reservas (si no, cerrar) |
| `POST` | `/api/companies/me/flight-departures/{id}/close` | Dejar de vender esa salida |
| `GET` | `/api/companies/me/reservations` | Existente: suma los ítems de vuelo con pasajeros y `issuanceStatus` |
| `POST` | `/api/companies/me/reservation-items/{id}/issuance` | `{ ticketNumbers: string[], bookingReference?, note? }` → `ISSUED`. Sólo con la reserva `CONFIRMED` |
| `POST` | `/api/companies/me/reservation-items/{id}/issuance-failure` | `{ reason }` → `FAILED`, cancela el ítem con reembolso total (simulado) y libera asientos |

### Admin

| Método | Ruta | Para qué |
|---|---|---|
| `PUT` | `/api/admin/companies/{id}/flight-seller` | `{ authorized, registration }` — autorizar / revocar la venta de vuelos |
| `GET` | `/api/admin/flights` | Todos los vuelos de operador, filtrables por empresa y estado |
| `POST` | `/api/admin/flights/{id}/suspend` · `/unsuspend` | Moderación: un vuelo suspendido sale de la búsqueda |
| `GET` | `/api/admin/reservations` | Existente: suma filtro `productType=FLIGHT` y la alerta "pagado sin emitir hace más de 24 h" |

### Estados y palabras

| `issuanceStatus` | Turista ve | Operador ve |
|---|---|---|
| `PENDING` | "Pago confirmado · emisión pendiente por {operador}" | "Emitir pasaje" |
| `ISSUED` | "Pasaje emitido por {operador}" + números de pasaje | "Emitido" |
| `FAILED` | "El operador no pudo emitir · reembolso total" | "Emisión fallida" |

En el entorno de demostración todo vuelo lleva además el rótulo "DEMO": un número de pasaje cargado por
un operador de prueba no es un pasaje real, y la app no lo presenta como tal.

## 4. Orden de trabajo y dependencias

```
B1  DTOs + firmas de endpoints (sin migración)  ──► regenerar api-client ──┬─► C  (Backoffice operador + admin)
                                                                           └─► A2 (app: elegir, pasajeros, pagar, Mis viajes)
B2  migración 0015 + servicios + tests de integración ───────────────────────► D  (integración y E2E)
```

1. **B1 primero** (corto): contratos en código, para que C y A2 trabajen contra los tipos generados.
2. **B2, C y A2 en paralelo.** B2 es el único que toca entidades, `TurisClickDbContext`,
   `ReservationService` y migraciones.
3. **`schema.ts` se regenera, no se mergea**: ante un conflicto, se toma cualquiera de las dos versiones y
   se vuelve a correr `npm run generate:types` contra la API de la rama integrada.
4. **D**: rama de integración; E2E completo (operador publica → turista busca → reserva → paga →
   operador registra emisión → turista ve el pasaje), cancelación con y sin emisión, expiración que libera
   asientos, dos turistas por el último asiento, y la suite completa de backend y de las dos apps.

### Propiedad de archivos (sesiones en paralelo)

| Sesión | Sólo esta edita |
|---|---|
| **B — backend** | `src/TurisClick.Api/**` (incluidas `Infrastructure/Database/Migrations/*` y `TurisClickDbContext.cs`), `tests/TurisClick.Api.Tests/**`, `.github/scripts/schema_preflight.py`, `frontend/packages/api-client/**` (incluido `schema.ts`, que sólo B regenera) |
| **C — Backoffice** | `frontend/apps/backoffice/**`: nuevo `src/modules/provider-flights/*`, emisión en `src/modules/provider-reservations/*`, vuelos y autorización en `src/modules/platform/*` / `src/modules/companies/*`, rutas y navegación |
| **A2 — app** | `frontend/apps/tourist-mobile/**`: nuevo `src/features/flights/booking/*`, tarjeta reservable en `src/features/flights/search/*`, ítem de vuelo en `src/features/reservations/*` y Mis viajes, rutas nuevas en `app/` |
| **D — integración** | rama `integration/turisclick-flights`, `tools/provider-flight-e2e`, `docs/**` |

- Nadie fuera de B corre `dotnet ef migrations add` ni toca entidades. Si C o A2 necesitan un cambio de
  contrato, lo piden a B; no lo inventan en el cliente.
- `schema.ts` se regenera, no se mergea: ante un conflicto se toma cualquier versión y B vuelve a correr
  `npm run generate:types` contra la API de la rama integrada.
- Mientras B2 no exista, C y A2 prueban contra respuestas simuladas (mocks del `httpClient`) y manejan el
  501 `FEATURE_NOT_AVAILABLE` con un mensaje claro, no como un error genérico.
- La release administrativa (moderación, suspensión de empresas, cancelación por el operador) no se
  reescribe: B rebasa sobre la versión final, con el arreglo P0 de pago/cancelación incluido.

Despliegue: la migración 0015 requiere autorización para `turisclick_db_v2` y el preflight de esquema
(`schema_preflight.py`) actualizado. Ninguna parte toca V1.

## 5. Lo que ya está (Workstream A)

- Autocompletado de aeropuertos del mundo (`GET /api/airports/search`, OurAirports embebido) por ciudad,
  nombre, país o IATA; como destino, sólo los aeropuertos de Bolivia con servicio regular.
- `POST /api/flights/search` con adultos, edades de niños, infantes y cabina; validación de destino en
  Bolivia, misma ciudad, 9 pasajeros, infantes ≤ adultos, horizonte de 330 días. Compatible con el contrato
  anterior (`passengers`).
- `inventoryMode` (`SIMULATED` / `SANDBOX` / `LIVE`) y `bookable: false` en la respuesta; la app rotula
  DEMO los vuelos simulados en la lista y en cada oferta.
- La reserva aérea del paquete ya no dice "Pasaje emitido": dice si es simulada, de prueba o confirmada.
