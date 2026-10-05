# Vuelos en TurisClick — propuesta técnica

Estado: **el flujo completo está implementado**, de la configuración del operador a la emisión del pasaje.
Probado de punta a punta contra Duffel en modo de prueba (ver `docs/package-flight-e2e-results.md`).

| Implementado | Pendiente |
|---|---|
| `IFlightProvider`, `DuffelFlightProvider`, `FakeFlightProvider` | Pago real (hoy el cobro es simulado) |
| `PackageFlightRule`, `FlightQuote`, `FlightBooking` + migraciones 0010 y 0011 | Cancelación de una reserva ya confirmada (política de reembolso) |
| Cotizar, revalidar, reservar, emitir y reconciliar | Ofertas que exigen documento de identidad |
| Regla en el Backoffice, búsqueda y checkout en Tourist Mobile | Integración con el agente de IA (§14) |

**Proveedor elegido: Duffel, en modo de prueba.** La evidencia de que funciona —búsqueda, revalidación,
orden y cancelación reales— está en [`duffel-test-results.md`](duffel-test-results.md).

Objetivo: que una agencia pueda publicar "Jordania Mágica — 7 días / 6 noches — desde USD 2.490" con el pasaje incluido, y que el turista compre sin que nadie del otro lado tenga que cotizar a mano. El vuelo no es un texto en la descripción: es un componente con precio vivo que se revalida antes de cobrar.

---

## 1. Comportamiento de producto

### La regla que ordena todo

**El precio del paquete con vuelo no existe hasta que se revalida.** Lo que se publica es el precio terrestre más una estimación; lo que se cobra sale de una consulta hecha minutos antes de confirmar. Esto no es una limitación de la integración: es la naturaleza del inventario aéreo, y la interfaz tiene que decirlo con todas las letras en lugar de disimularlo.

De ahí salen tres estados de precio que el turista ve con nombres distintos:

| Estado | Qué es | Dónde aparece |
|---|---|---|
| **Desde** | `Package.Price` (terrestre) + la oferta aérea más barata encontrada en la última búsqueda | Catálogo, ficha del paquete |
| **Cotizado** | Oferta concreta para la salida y la cantidad de viajeros elegidas, con su vencimiento | Al elegir salida y pasajeros |
| **Confirmado** | Resultado de revalidar esa oferta contra el proveedor, con el precio que se va a cobrar | Pantalla de aceptación, antes de pedir datos de pasajeros |

Un paquete sin vuelo sigue funcionando exactamente como hoy; nada de esto se le aplica.

### Lo que el producto promete y lo que no

- Promete: buscar, cotizar, revalidar, reservar y mostrar el estado real de la reserva aérea.
- **No promete emitir el ticket.** En modo de prueba una orden de Duffel es una reserva confirmada con su localizador, no un pasaje emitido, y el documento lo dice con todas las letras. Decir "ticket emitido" cuando no lo está sería la peor mentira posible en este dominio.

---

## 2. Por qué Duffel, y qué se verificó

### 2.1 Amadeus quedó fuera, y no por una cuestión técnica

La investigación previa de este documento apuntaba a las **Self-Service APIs de Amadeus**. Dos hallazgos
la dieron de baja como objetivo de implementación:

1. El portal Self-Service fue **dado de baja el 17 de julio**; el acceso pasó al portal Enterprise y está
   mediado por un alta comercial.
2. De forma consistente con eso, `test.api.amadeus.com` **ya no resuelve**: el nombre existe en la zona
   pero no tiene registros A, AAAA ni CNAME, y lo responde el DNS autoritativo de Amadeus
   (`mucdns01.amadeus-dns.com`), no un bloqueo de nuestra red. Verificado desde dos redes y contra el
   resolutor público de Google.

Amadeus Enterprise **sigue siendo un proveedor posible a futuro**, por otra vía de acceso. Nada de lo
construido acá se pierde si algún día se suma: entraría como un `AmadeusFlightProvider` más.

### 2.2 Lo que Duffel documenta, verificado en su documentación vigente

| Tema | Qué dice la documentación oficial |
|---|---|
| Autenticación | `Authorization: Bearer <token>`; los tokens de prueba empiezan con `duffel_test_` y sólo ven recursos de prueba |
| Versionado | Cabecera obligatoria `Duffel-Version: v2` en cada request |
| Búsqueda | `POST /air/offer_requests` con `slices`, `passengers` y `cabin_class` |
| Revalidación | `GET /air/offers/{id}`; la documentación advierte que *"you may see changes to the offer (e.g a changed `total_amount`)"* y que los precios de búsqueda no están garantizados al reservar |
| Vencimiento | Cada oferta trae `expires_at`, típicamente 15–30 minutos; Duffel recomienda comprobarlo antes de crear la orden |
| Orden | `POST /air/orders` con `selected_offers`, `passengers` y `payments` |
| Pago en prueba | El saldo de la cuenta es **ilimitado** en modo de prueba y el pago se declara como `type: "balance"` — sin tarjeta y sin dinero |
| Modo de prueba | Las ofertas y órdenes vienen con `live_mode: false` |
| Cancelación | Dos pasos: `POST /air/order_cancellations` y luego `.../actions/confirm` |
| Límites | Las respuestas traen `ratelimit-limit` y `ratelimit-reset`; la búsqueda en vivo ronda 10 pedidos por 60 s |
| Idempotencia | **No encontré** un mecanismo de clave de idempotencia documentado para crear órdenes. Se marca como no verificado y el diseño no se apoya en él (§9) |

### 2.3 Rutas de escenario: el regalo de Duffel para probar lo difícil

Duffel documenta rutas que **provocan comportamientos concretos** en modo de prueba, lo que permite
ejercitar los caminos de error contra la API real y no sólo contra un mock:

| Ruta | Comportamiento |
|---|---|
| `PVD → RAI` | no devuelve ofertas |
| `LHR → STN` | el precio cambia al revalidar |
| `LGW → LHR` | oferta vencida |
| `LHR → LGW` | error al crear la orden |
| `LGW → STN` | saldo insuficiente |
| `STN → LHR` | timeout garantizado |
| `JFK → EWR` | ofertas que no exigen pago inmediato |
| `LHR → DXB` | vuelos con escalas |

### 2.4 Lo que la corrida real confirmó (y lo que corrigió)

Detalle completo en [`duffel-test-results.md`](duffel-test-results.md). Tres cosas que sólo se supieron
llamando a la API:

1. **Las rutas domésticas de Bolivia SÍ devuelven ofertas** en modo de prueba: VVI→LPB, VVI→CBB,
   LPB→VVI, LPB→CBB y CBB→VVI, 20 ofertas cada una. Duffel Airways (`ZZ`) es una aerolínea sintética
   que vuela a los aeropuertos que se le pidan, así que la demo de la tesis **no necesita cambiarse a
   una ruta europea**. Los horarios y precios no son realistas, y eso se dice.
2. **Los horarios vienen en hora local del aeropuerto y sin huso.** Tipar eso como `DateTimeOffset`
   hacía que el servidor les aplicara su propio offset y corriera todos los vuelos cuatro horas. El
   modelo usa `DateTime` sin huso a propósito.
3. **Duffel rechaza un apellido con dígitos** (`Field 'family_name' has invalid format`). Los pasajeros
   sintéticos tienen nombres sin números, y el caso quedó cubierto por un test.

### 2.5 Lo que queda sin verificar

- Si existe un mecanismo oficial de idempotencia al crear órdenes.
- El comportamiento de emisión y de reembolso en modo **live**: este spike no lo tocó y no lo va a tocar.
- Cobertura real de aerolíneas bolivianas en modo live (en prueba, lo que responde es Duffel Airways).
- **KIU**, de interés para Bolivia y la región, no fue investigado: no afirmo nada sobre su API.

## 3. Arquitectura propuesta

### Una interfaz, dos implementaciones, cero DTOs de Amadeus en el dominio

Mismo patrón que ya probó bien con `IAiModelClient` (ver [`ai-rag-architecture.md`](ai-rag-architecture.md)): el dominio habla un lenguaje propio y el proveedor se elige por configuración.

```
Modules/Flights/
  FlightsModuleExtensions.cs            <- Flights:Provider elige la implementación
  Services/
    IFlightProvider.cs                  <- el contrato del dominio
    FlightModels.cs                     <- modelos propios, sin una palabra de ningún proveedor
    FlightProviderExceptions.cs         <- una excepción por decisión, no por código HTTP
    FlightsOptions.cs
    Providers/
      FakeFlightProvider.cs             <- determinístico: tests, demo y entorno desplegado
      Duffel/
        DuffelFlightProvider.cs         <- HTTP + mapeo
        DuffelDtos.cs                   <- `internal`: ningún tipo sale de esta carpeta
```

Mañana, sin tocar el dominio: `Providers/Kiu/KiuFlightProvider.cs` (interesante para Bolivia y la
región, todavía sin investigar), `Providers/Amadeus/AmadeusFlightProvider.cs` (Enterprise), o el que
venga.

```csharp
public interface IFlightProvider
{
    string Name { get; }
    Task<FlightSearchResult> SearchAsync(FlightSearchRequest request, CancellationToken ct);
    Task<FlightOffer> RefreshOfferAsync(string offerId, CancellationToken ct);
    Task<FlightOrderResult> CreateOrderAsync(FlightOrderRequest request, CancellationToken ct);
    Task<FlightOrderResult?> GetOrderAsync(string orderId, CancellationToken ct);
}
```

Esto ya está implementado y probado: 29 tests sobre el adapter y el proveedor falso, ninguno de los
cuales toca la red.

`GetBookingAsync` no es decorativo: es lo que permite reconciliar una orden que quedó huérfana (§4.4).

Tres decisiones que vale la pena explicitar:

1. **No se guarda la respuesta cruda del proveedor.** Era necesario con el flujo de Amadeus, que exige
   reenviar el objeto de oferta completo; **con Duffel alcanza el `offer_id` opaco**, que es lo único
   que viaja entre buscar, revalidar y reservar. Menos datos guardados, menos superficie que proteger.
   Si un proveedor futuro exigiera el payload, se guarda en ese adapter, no en el dominio.
2. **El cliente nunca manda una oferta.** Manda el id de nuestra `FlightQuote`. Si el payload viajara al navegador y volviera, cualquiera podría cambiar el precio.
3. **Selección por configuración**, igual que la IA: `Flights__Provider = Fake | Duffel`. El default es
   `Fake` y el entorno desplegado se queda ahí hasta que exista una decisión explícita.
4. **Candado contra el modo real.** El adapter **no arranca** si el token no empieza con `duffel_test_`,
   salvo que alguien ponga `Flights:Duffel:RequireTestToken` en false a propósito. Una reserva aérea
   real cuesta dinero: el error tiene que saltar en el arranque, no en la primera venta.

---

## 4. Modelo de dominio propuesto

### 4.1 Configuración del vuelo en el paquete

```csharp
public class PackageFlightRule          // 1–1 opcional con Package
{
    public Guid PackageId { get; set; }
    public string DestinationIata { get; set; }      // a dónde vuela el paquete
    public IReadOnlyList<string> OriginIatas { get; }// orígenes permitidos (el turista elige)
    public CabinClass Cabin { get; set; }            // ECONOMY por defecto
    public int OutboundOffsetDays { get; set; }      // respecto de PackageAvailability.DepartureDate
    public int InboundOffsetDays { get; set; }       // normalmente DurationDays - 1
    public int MaxStops { get; set; }
    public bool NonStopPreferred { get; set; }
}
```

`Package.IncludesFlight` (bool) es el interruptor; la regla existe si y solo si está en `true`. Las fechas **no se cargan a mano**: se derivan de la salida del paquete más los offsets, que es lo que evita que el proveedor configure un vuelo que no coincide con su propia salida.

### 4.2 La cotización

```csharp
public class FlightQuote
{
    public Guid Id { get; set; }
    public Guid PackageId { get; set; }
    public Guid PackageAvailabilityId { get; set; }  // la salida concreta
    public string OriginIata / DestinationIata { get; set; }
    public int Travelers { get; set; }

    public decimal TotalPrice { get; set; }          // total de la oferta, no por persona
    public string Currency { get; set; }
    public decimal PricePerTraveler { get; set; }

    public string Provider { get; set; }             // "Duffel" | "Fake"
    public string ProviderOfferRef { get; set; }
    public string ProviderPayload { get; set; }      // jsonb; nunca sale del backend
    public FlightItinerarySummary Summary { get; set; } // tramos legibles: horarios, escalas, aerolínea

    public DateTimeOffset ValidUntil { get; set; }   // MIN(nuestro TTL, dato del proveedor)
    public DateTimeOffset CreatedAt { get; set; }
    public FlightQuoteStatus Status { get; set; }    // SEARCHED | REVALIDATED | EXPIRED | CONSUMED
}
```

### 4.3 La reserva aérea: entidad propia, no un `ReservationItem`

**Decisión con consecuencias, así que la justifico.** La tentación es agregar `FLIGHT` a `ProductType` y meter el vuelo como una línea más. No conviene:

- `ReservationItem.CompanyId` es obligatorio y está denormalizado para el aislamiento por empresa (UC-SYS-03). Un vuelo no tiene empresa de TurisClick: habría que hacerlo nullable y debilitar esa garantía.
- La restricción `ck_reservation_items_product_shape` exige exactamente una availability nuestra por línea. Un vuelo no tiene ninguna.
- Los proveedores verían vuelos en su listado de reservas recibidas. No son suyos.
- El ciclo de vida es ajeno: una reserva aérea puede cancelarse, caducar o emitirse del lado del proveedor sin que nosotros hagamos nada.

Propuesta: **`FlightBooking`, 1–1 con `Reservation`** (índice único sobre `reservation_id`, igual que el `ai_itinerary_id` de hoy).

```csharp
public class FlightBooking
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }          // único
    public Guid FlightQuoteId { get; set; }

    public FlightBookingStatus Status { get; set; }
    // PENDING -> CONFIRMED -> (TICKETED) | FAILED | CANCELLED
    public string? ProviderOrderId { get; set; }     // id de la orden
    public string? RecordLocator { get; set; }       // PNR, lo que el turista necesita

    public decimal TotalPrice { get; set; }          // snapshot de lo revalidado
    public string Currency { get; set; }

    public string IdempotencyKey { get; set; }       // nuestro, previo a llamar al proveedor
    public DateTimeOffset CreatedAt / ConfirmedAt / FailedAt { get; set; }
    public string? FailureReason { get; set; }
}
```

El total de una reserva pasa a ser `suma(items) + flightBooking?.TotalPrice`, calculado en la capa de lectura. Es coherente con la decisión que ya existe de que `Reservation` no persiste un total (puede haber varias monedas).

### 4.4 Pasajeros

Tabla aparte, `FlightTraveler`, con el mínimo que la oferta exige según `bookingRequirements`, ligada a `FlightBooking` y borrable sin tocar la reserva. Detalle en §10.

---

## 5. Cambios de base de datos (propuestos, no ejecutados)

Una sola migración aditiva, sin tocar una columna existente salvo el flag del paquete:

| Cambio | Tipo |
|---|---|
| `packages.includes_flight boolean not null default false` | aditivo |
| `package_flight_rules` (PK = package_id) | tabla nueva |
| `flight_quotes` | tabla nueva (jsonb para el payload) |
| `flight_bookings` (único sobre `reservation_id`) | tabla nueva |
| `flight_travelers` | tabla nueva |
| enums `flight_quote_status`, `flight_booking_status`, `cabin_class` | tipos nuevos |

Cero cambios destructivos, cero columnas renombradas, cero `ReservationItem` tocado. El catálogo y las reservas existentes siguen funcionando sin leer una sola de estas tablas.

---

## 6. Endpoints potenciales

| Método | Ruta | Para quién | Estado |
|---|---|---|---|
| `PUT` | `/api/packages/{id}/flight-rule` | Operador: configurar el vuelo | ✅ |
| `DELETE` | `/api/packages/{id}/flight-rule` | Operador: dejar de incluirlo | ✅ |
| `GET` | `/api/packages/{id}/flight-rule` | Operador (los suyos) o Admin (todos) | ✅ |
| `POST` | `/api/packages/{id}/flight-quotes` | Público: cotizar (crea `FlightQuote`) | ✅ |
| `POST` | `/api/flight-quotes/{id}/revalidate` | Público: precio vigente antes de comprar | ✅ |
| `GET` | `/api/airports` | Catálogo de aeropuertos conocidos | ✅ |
| `POST` | `/api/reservations` | Suma `flightQuoteId`; retiene cupo y registra la intención de vuelo | ✅ |
| `POST` | `/api/reservations/{id}/pay` | Suma `travelers[]` y `acceptedFlightPrice`; revalida, cobra y emite | ✅ |
| `GET` | `/api/reservations/{id}` y `/me` | Devuelven el bloque `flight` con su estado y localizador | ✅ |

`GET /api/packages/{id}` suma `includesFlight`, el destino del vuelo y los orígenes habilitados: es lo
único de la regla que el catálogo público necesita. Los desfases de fecha son configuración interna y
no salen.

Ningún endpoint existente cambia de forma: `CreateReservationRequest` suma campos **opcionales**, y su validación actual (exactamente uno entre experiencia y paquete) se mantiene.

---

## 6.1 Casos de uso

| Caso | Quién | Estado |
|---|---|---|
| **Configurar las reglas de vuelo de un paquete** | Operador | ✅ implementado |
| **Inspeccionar las reglas de vuelo de cualquier paquete** | Admin | ✅ implementado |
| **Cotizar el vuelo de un paquete** | Turista (público) | ✅ implementado |
| **Revalidar una cotización antes de comprar** | Turista (público) | ✅ implementado |
| **Reservar paquete + vuelo en una sola operación** | Turista | ✅ implementado |
| **Pedir un vuelo desde el asistente** | Turista | ⏳ pendiente (§14) |

## 7. Flujo del Provider

1. Crea el paquete como hoy.
2. Marca "incluye vuelo" y configura: destino, orígenes permitidos, cabina, offsets respecto de la salida, escalas máximas.
3. Al guardar, el backend **valida contra el proveedor** que la ruta devuelva al menos una oferta para la próxima salida. Si no devuelve nada, se avisa en el momento en lugar de descubrirlo cuando un turista intente comprar.
4. Las salidas (`PackageAvailability`) siguen siendo las de siempre: el vuelo se deriva de ellas, no al revés.
5. En el listado, un paquete con vuelo muestra "desde" y la aclaración de que el aéreo se confirma al reservar.

---

## 8. Flujo del Tourist

1. Abre el paquete. Ve **desde USD 2.490** y, explícito, que incluye aéreo y que el precio final se confirma al reservar.
2. Elige salida, origen y cantidad de viajeros.
3. TurisClick busca (`SearchAsync`), persiste la mejor opción como `FlightQuote` y muestra **precio cotizado** con horarios, escalas y vencimiento.
4. El turista pide reservar. TurisClick **revalida** (`RevalidateAsync`):
   - mismo precio → sigue;
   - cambió → se muestra la diferencia y se pide aceptación explícita, con el mismo vocabulario que ya usa el checkout;
   - venció o desapareció → se vuelve a buscar y se cotiza de nuevo.
5. Se piden los datos de pasajeros, **solo los que `bookingRequirements` marca como obligatorios**.
6. Reserva, en este orden exacto (§9).
7. Confirmación: localizador, tramos, precio final y, con todas las letras, que la emisión del ticket es un paso posterior.

---

## 9. Orden de operaciones, fallos parciales e idempotencia

El problema central: nuestra base es transaccional y el proveedor aéreo no. No se puede meter una llamada HTTP dentro de una transacción de Postgres y pretender atomicidad.

**Orden implementado.** El pasaje se emite en el PAGO, no al crear la reserva, y ese orden es el único
defendible: emitir antes de cobrar deja un pasaje comprado para alguien que puede no pagar nunca, y cobrar
antes de confirmar que el vuelo existe deja a alguien pagando algo que no se le puede dar.

1. **Crear la reserva** (`POST /api/reservations` con `flightQuoteId`) — una transacción local: valida la
   cotización, toma el cupo con el `UPDATE` condicional que ya existía y escribe `FlightBooking` en
   `PENDING` con su clave de correlación y el snapshot de ruta y fechas. No se llama al proveedor.
2. **Pagar** (`POST /api/reservations/{id}/pay`):
   1. revalidar la oferta contra el proveedor — si cambió el precio, se corta acá y se pide aceptación;
   2. validar los datos de los pasajeros;
   3. autorizar el pago (hoy simulado);
   4. `PENDING → ORDERING` con un `UPDATE` condicional, **commiteado antes de llamar**;
   5. crear la orden en el proveedor, sin ninguna transacción abierta;
   6. una **única** transacción local escribe el desenlace del vuelo y el destino de la reserva.

El paso 4 es el que hace recuperable el peor caso. Si el proceso muere entre la llamada y la escritura,
queda una fila en `ORDERING` con su clave, y el reconciliador le pregunta al proveedor si la orden existe.
Sin esa fila, una orden creada sin rastro nuestro es el pasaje de alguien perdido en el aire.

El paso 6 es una sola escritura por una razón concreta: si el vuelo se confirmara en una transacción y la
reserva en otra, una caída en el medio dejaría un pasaje emitido con una reserva sin confirmar.

### 9.1 Máquina de estados de `FlightBooking`

```
                ┌───────────── el turista cancela / la reserva expira ──────────► CANCELLED
                │
  (crear) ─► PENDING ─► ORDERING ─┬─► CONFIRMED                 el proveedor confirmó la orden
                                  ├─► PENDING                   la conexión nunca salió: se reintenta
                                  ├─► FAILED                    rechazo definitivo: se libera el cupo
                                  └─► RECONCILIATION_REQUIRED ─┬─► CONFIRMED   la orden existía
                                                               ├─► FAILED      no existe ninguna
                                                               └─► CANCELLED   existía pero sin reserva
```

Ninguna transición la escribe el cliente: cada una la gana el servidor con un `UPDATE` condicional, igual
que las de `Reservation`. Un vuelo en `ORDERING` o `RECONCILIATION_REQUIRED` **bloquea** el reintento del
pago, la cancelación y la expiración de la reserva: reintentar una emisión sin saber si ocurrió es
exactamente lo que compra dos pasajes.

### 9.2 Qué pasa en cada falla

| Falla | Qué hace el sistema |
|---|---|
| No hay cupo en el paquete | Nada se reserva y no se llama al proveedor |
| La cotización venció o es de otra persona | Se rechaza al crear la reserva; no se toca el cupo |
| El precio cambió | Se frena ANTES de cobrar y se exige aceptar el importe vigente |
| La oferta ya no existe al revalidar | 410 con el motivo; el cupo sigue retenido para que la persona decida |
| La oferta exige documento de identidad | No se vende: se dice que esa opción todavía no se puede emitir |
| El proveedor rechaza la emisión (4xx) | `FAILED`, se libera el cupo, se cancela la reserva y se revierte el cobro |
| La conexión nunca se abrió | Vuelve a `PENDING`: no hay nada emitido y el cupo se mantiene para reintentar |
| **Desenlace desconocido** (timeout) | `RECONCILIATION_REQUIRED`: no se libera cupo ni se reintenta; decide el reconciliador |
| La orden sale bien y la reserva ya no es confirmable | `RECONCILIATION_REQUIRED` con el localizador: el reconciliador cancela el pasaje huérfano |
| El turista toca dos veces | La cotización ya reservada devuelve la reserva existente (índice único) |

### 9.3 Idempotencia

Duffel **no documenta** un mecanismo de idempotencia para `POST /air/orders`, así que no se finge que lo
tenga. La idempotencia es nuestra y tiene tres capas:

1. **Una cotización se reserva una vez.** `ux_flight_bookings_flight_quote_id` lo garantiza en la base: el
   doble toque y el reintento del cliente tras un timeout devuelven la reserva que ya existe en vez de
   retener cupo otra vez.
2. **Una reserva emite una vez.** La transición condicional `PENDING → ORDERING` es el candado: de dos
   pagos simultáneos, sólo uno sale con permiso para emitir.
3. **Una clave de correlación propia** (`idempotency_key`), escrita antes de llamar y enviada como
   `metadata` de la orden — un campo libre que Duffel guarda y devuelve sin usarlo. Es lo que permite
   reconocer la orden cuando se perdió su respuesta.

### 9.4 Reconciliación

`FlightReconciliationService`, invocado por un `BackgroundService` cada 60 s (apagable por configuración,
y apagado en los tests). Procesa sólo lo que no se puede resolver de otra forma: `RECONCILIATION_REQUIRED`
con su espera cumplida, y `ORDERING` abandonado más de 5 minutos —el proceso que se cayó en el peor
momento—.

Para cada caso le pregunta al proveedor si la orden existe: con el id, `GetOrderAsync`; sin él,
`FindOrderByOfferAsync`, que lista las órdenes recientes de la cuenta y empareja por `offer_id` o por
nuestra clave en `metadata` (los dos campos los devuelve el objeto orden; `GET /air/orders` acepta
`limit` hasta 200). **Nunca reintenta la compra para averiguar qué pasó.**

- **Existe y la reserva sigue pendiente** → se confirman el vuelo y la reserva en una transacción.
- **Existe y la reserva ya no está vigente** → se cancela la orden en el proveedor y queda el rastro.
- **No existe** → hasta 4 consultas con espera creciente (1, 5 y 15 minutos) antes de declararla
  inexistente. Recién entonces: `FAILED`, se libera el cupo y se cancela la reserva. Decir "no existe" de
  más es liberar un cupo que sí se vendió.

**Concurrencia:** dos turistas sobre la misma salida compiten por el cupo con el `UPDATE` condicional existente, que ya es la autoridad. El vuelo no agrega una carrera nueva: cada uno reserva su propia oferta, y si el inventario aéreo se agotó, el proveedor rechaza y cae en el caso "el proveedor rechaza la emisión".

---

## 10. Seguridad y datos personales

1. **Mínimo indispensable.** Se pide lo que `bookingRequirements` exige para *esa* oferta. Nada "por las dudas".
2. **Pasaporte solo si la oferta lo exige**, en `flight_travelers`, y se borra cuando el viaje termina (retención explícita, no "para siempre").
3. **Nunca en logs.** Ni nombres, ni documentos, ni el payload del proveedor. El logging de esta integración registra ids y estados.
4. **Nunca datos reales en sandbox.** Decisión nuestra (§2.4): mientras `Flights__Provider` no sea producción, el backend **rechaza** datos de pasajero que no vengan del set sintético. Es una validación, no una recomendación en un documento.
5. **Credenciales por configuración**, igual que hoy: user-secrets en local, Key Vault en Azure, nunca en el repositorio. Esta fase no agrega ningún secreto.
6. El `ProviderPayload` se guarda cifrado en reposo si llegara a contener datos de pasajero; en la fase de búsqueda todavía no los tiene.

---

## 11. Test vs Production

| | Local / demo | Azure |
|---|---|---|
| `Flights__Provider` | `Duffel` con el token de prueba en User Secrets | **`Fake`** |
| Credenciales | user-secrets | ninguna hasta decisión explícita |
| Datos de pasajero | sintéticos, validados | sintéticos |

El proveedor falso no es un mock pobre: devuelve itinerarios verosímiles y determinísticos, simula cambio de precio al revalidar, vencimiento de oferta y fallo de reserva. Es lo que permite demostrar y testear los caminos difíciles —que son los que importan— sin depender de un sandbox que hoy ni siquiera resuelve.

---

## 12. Estrategia de testing

1. **Unitarios del dominio**, sin red: cálculo del precio total, vencimiento, transiciones de `FlightBookingStatus`, derivación de fechas desde la salida del paquete.
2. **Orquestación con `FakeFlightProvider`**: el camino feliz y los cuatro fallos de la tabla de §9, incluida la reserva que sobrevive a la caída entre el paso 2 y el 3.
3. **Contrato del adapter contra fixtures grabadas**: respuestas reales del sandbox, capturadas una vez en el spike, guardadas como JSON y reproducidas con un `HttpMessageHandler` falso. Verifican el mapeo sin tocar la red.
4. **CI nunca llama a Duffel.** Ni con credenciales: los 29 tests del módulo usan un `HttpMessageHandler` simulado y fixtures. Un test que depende de un tercero no es un test.
5. **Un spike manual, documentado y reproducible**, para capturar esas fixtures (fase 0).
6. Los 505 tests de backend y los 333 de frontend siguen verdes: esta integración no toca sus caminos.

---

## 13. Plan de implementación por fases

| Fase | Qué incluye | Depende de |
|---|---|---|
| **0 — Spike** ✅ | `IFlightProvider`, `DuffelFlightProvider`, `FakeFlightProvider`, 29 tests y el ciclo completo probado contra la API real | hecho |
| **1 — Dominio** | Entidades (`PackageFlightRule`, `FlightQuote`, `FlightBooking`), migración aditiva, endpoints de cotización y UI del operador para configurar el vuelo | — |
| **2 — Reserva** | Orquestación de §9, reconciliación, formulario de pasajeros, confirmación | Fase 1 |
| **3 — IA** | El vuelo como herramienta del agente (§14) | Fase 2 |

Cada fase termina con tests verdes y puede quedar en `master` sin activar nada: el flag apagado deja el comportamiento actual intacto.

---

## 14. Cómo encaja con el agente de IA

La regla no cambia: **el modelo no inventa nada**. Lo que cambia es de dónde salen los datos.

- El catálogo sigue viniendo del RAG estructurado sobre PostgreSQL ([`ai-rag-architecture.md`](ai-rag-architecture.md)).
- Los vuelos **no son documentos recuperables**: son el resultado de una herramienta. El agente pide una cotización, el backend llama a `IFlightProvider`, y lo que vuelve entra al prompt como contexto de candidatos, con su id.
- El modelo solo puede referirse a una `FlightQuote` que nosotros creamos. Igual que hoy con los productos, una cotización que el modelo "nombre" y no exista en el conjunto ofrecido se descarta en la capa de validación (mismo lugar donde hoy se filtran los ids inventados).
- Un itinerario de IA podría llevar un vuelo asociado con el mismo criterio que la reserva: referencia a `FlightQuote`, no un ítem más.
- El precio del vuelo en un itinerario es **estimado** por definición, y se revalida al reservar — que es exactamente lo que ya hace `ItineraryRevalidationService` con el resto.

### El punto de integración exacto (pendiente, a propósito)

No se implementó en esta oleada para no mezclar dos cambios grandes, pero el lugar ya está identificado
y es uno solo:

1. `PreferenceExtractionResult` suma un campo opcional `OriginAirportIata`. El modelo ya extrae destino
   y fechas; reconocer "salgo desde Santa Cruz" es el mismo tipo de señal, sobre el mismo vocabulario
   cerrado (los códigos de `AirportCatalog`).
2. `AiConversationService`, donde hoy llama a `RetrievalService`, suma: si el paquete candidato tiene
   `IncludesFlight` y la conversación trae un origen válido, llamar a `IPackageFlightService.QuoteAsync`
   y adjuntar las opciones como contexto de candidatos.
3. El prompt recibe esas opciones **como datos**, igual que los productos del catálogo. El modelo puede
   explicarlas y elegir entre ellas; no puede inventar una.
4. La validación posterior ya existe conceptualmente: igual que hoy se descarta un `productId` que no
   estaba entre los candidatos ofrecidos, se descarta un `quoteId` que no salga de la cotización que el
   backend acaba de hacer.

Lo que **no** cambia: el LLM sigue sin poder inventar aerolínea, horario, precio, disponibilidad,
equipaje ni localizador. El inventario aéreo entra por `IFlightProvider`, nunca por RAG ni por
generación.
