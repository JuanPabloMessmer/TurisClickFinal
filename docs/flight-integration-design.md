# Vuelos en TurisClick — propuesta técnica

Estado: **diseño, sin implementar**. Ningún cambio de dominio, de base ni de infraestructura salió de este documento todavía.

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
- **No promete emitir el ticket.** Las Self-Service APIs de Amadeus no emiten: hace falta un consolidador (§2.5). Un paquete con vuelo queda `RESERVADO` y la emisión es un paso operativo documentado, no una promesa de la interfaz. Decir "ticket emitido" cuando no lo está sería la peor mentira posible en este dominio.

---

## 2. Investigación Amadeus — qué verifiqué y cómo

Toda afirmación de esta sección tiene su fuente. Lo que no pude verificar está marcado como **sin verificar**, y no se usa como base de ninguna decisión.

### 2.1 El hallazgo que condiciona el plan: `test.api.amadeus.com` no resuelve

Verificado el 2026-10-05, desde dos redes distintas y contra el DNS autoritativo de Amadeus:

| Consulta | Resultado |
|---|---|
| `curl https://test.api.amadeus.com/...` (entorno de herramientas) | `Could not resolve host` |
| `Invoke-WebRequest` (red del host) | `The remote name could not be resolved` |
| `dns.google/resolve?name=test.api.amadeus.com&type=A` | `Status 0` (NOERROR) y **sin registros A** |
| idem `type=AAAA` y `type=CNAME` | sin registros; responde la autoridad `mucdns01.amadeus-dns.com` |
| `dns.google/resolve?name=api.amadeus.com&type=A` | resuelve a `45.60.161.120` (Imperva) |

El nombre existe en la zona pero **hoy no tiene dirección**: no es un bloqueo de nuestra red ni un NXDOMAIN, es NODATA desde el servidor autoritativo de Amadeus. La documentación y los SDK oficiales siguen describiendo ese host como el entorno de test ([SDK Python, sección de entornos](https://github.com/amadeus4dev/amadeus-python)), así que o el hostname fue retirado, o cambió la forma de acceder al sandbox.

**Consecuencia para el plan:** no puedo validar nada contra el sandbox desde acá, y **la primera tarea de la fase 0 es que vos confirmes la URL base vigente en el dashboard de Amadeus**, que solo se ve con sesión iniciada. Hasta entonces, todo lo que construyamos corre contra el proveedor falso (§3).

Dato adicional: `api.amadeus.com` sí responde, pero mi request fue rechazado por su WAF (`410 Gone`, *"This request was blocked by our security service"*, Imperva). Las llamadas desde entornos automatizados o IPs de datacenter pueden ser filtradas — es un riesgo a considerar para CI, no solo para mi caja.

### 2.2 Las tres operaciones del flujo de reserva

Verificado contra la especificación OpenAPI oficial ([amadeus4dev/amadeus-open-api-specification](https://github.com/amadeus4dev/amadeus-open-api-specification), archivada en julio de 2026 pero es la fuente formal de los contratos):

| API | Qué hace | Lo que importa para nosotros |
|---|---|---|
| **Flight Offers Search** (`GET /v2/shopping/flight-offers`) | *"Return list of Flight Offers based on searching criteria"* | Requeridos: `originLocationCode`, `destinationLocationCode`, `departureDate`, `adults`. `max` por defecto 250. Cada oferta trae `numberOfBookableSeats` (máx. 9), `lastTicketingDate`, `instantTicketingRequired`, `oneWay`, `price.grandTotal` |
| **Flight Offers Price** (`POST /v1/shopping/flight-offers/pricing`) | *"Confirm pricing of given flightOffers"* | Devuelve precio confirmado con impuestos y cargos, y **`bookingRequirements`**: qué datos de pasajero y de contacto son obligatorios para esa oferta concreta. Parámetros `include` (`credit-card-fees`, `bags`, `other-services`, `detailed-fare-rules`) y `forceClass` |
| **Flight Create Orders** (`POST /v1/booking/flight-orders`) | Convierte una oferta priceada en una reserva | Cuerpo: `flightOffers` (1–6), `travelers` (1–18), y opcionales `contacts`, `remarks`, `ticketingAgreement` (`CONFIRM` / `DELAY_TO_QUEUE` / `DELAY_TO_CANCEL`) |
| **Flight Order Management** (`GET`/`DELETE /v1/booking/flight-orders/{id}`) | Consultar o cancelar la reserva creada | Es la pieza que hace posible la reconciliación (§4.4) |

**Campos obligatorios de un `traveler`:** `id`, `dateOfBirth`, `name` (`firstName`, `lastName`) y `gender` (`MALE`/`FEMALE`/`UNSPECIFIED`/`UNDISCLOSED`). `contact` y `documents` existen en el esquema pero **no están marcados como requeridos**: cuáles hacen falta lo dice `bookingRequirements` de la respuesta de Price, por oferta. Es la diferencia entre pedirle el pasaporte a todo el mundo "por las dudas" y pedirlo solo cuando la aerolínea lo exige.

### 2.3 Vencimiento y revalidación de ofertas

La especificación de Price define `lastTicketingDate` / `lastTicketingDateTime` así: *"If booked on the same day as the search (with respect to timezone), this flight offer is guaranteed to be thereafter valid for ticketing until this date (included)"*.

Leído con cuidado, eso **no** es "la oferta vale hasta esa fecha". Es: si reservás el mismo día de la búsqueda, el ticketing queda garantizado hasta esa fecha. El precio de una búsqueda vieja no está garantizado. Por eso:

- toda cotización nuestra lleva **nuestro propio TTL corto** además del dato del proveedor;
- **siempre** se llama a Price antes de reservar, aunque el TTL no haya vencido;
- si Price devuelve otro precio, no se cobra: se le muestra al turista la diferencia y decide. Es el mismo patrón que ya usa el checkout actual con `acceptPriceChanges`, y conviene reusar su vocabulario.

### 2.4 Qué permite realmente el entorno de test

Verificado en el contenido oficial indexado ([guía de datos de test](https://developers.amadeus.com/self-service/apis-docs/guides/test-environment-data-collection-746), [tutorial de APIs de vuelos](https://developers.amadeus.com/self-service/apis-docs/guides/developer-guides/resources/flights/)) — las páginas se renderizan en el cliente y no se pueden citar textual con un fetch, así que lo marco como verificado por búsqueda, no por lectura directa:

- el entorno de test es **gratuito con datos limitados**: cacheados, de cobertura parcial o directamente falsos;
- *"our test environment is based on a subset of the production, if you are not returning any results try with big cities/airports like LON (London) or NYC (New-York)"* (esto sí está en la especificación OpenAPI, es cita literal);
- se pueden crear órdenes sin pago real, pero **el inventario es una copia del real**: reservar mucho lo vacía y deja de haber disponibilidad;
- el id que devuelve Flight Create Orders en test es **temporal**;
- límite de ~**10 TPS** en test (sin verificar contra una fuente citable; tratarlo como orden de magnitud, no como contrato).

**Sobre datos personales reales en test: no encontré una prohibición explícita de Amadeus.** No voy a inventar una regla y atribuírsela. Lo que sí está documentado es que el entorno es de desarrollo, con datos falsos y órdenes temporales — y nuestra decisión, propia, es **no enviar nunca datos de personas reales a un sandbox** (§10).

### 2.5 Emisión, pago y consolidador

El punto que más cambia el alcance del producto: **con Self-Service no se emiten tickets**. Según el tutorial oficial de vuelos, quien usa Self-Service tiene que trabajar con un **consolidador aéreo** que emita en su nombre, el pago se arregla directamente con el consolidador y **no pasa por la API**; agregar una forma de pago al cuerpo de Flight Create Orders es rechazado con `INVALID FORMAT`.

Para la tesis esto es una restricción, no un bloqueo: podemos demostrar el ciclo completo hasta la **reserva confirmada con su localizador**, que es exactamente donde termina el trabajo del software. La emisión es un paso comercial que requiere un acuerdo que TurisClick no tiene y no puede fingir.

### 2.6 Test vs Production, en una tabla

| | Test | Production |
|---|---|---|
| Base URL | `test.api.amadeus.com` **(hoy no resuelve — §2.1)** | `api.amadeus.com` |
| Datos | limitados, cacheados o falsos; subconjunto de producción | reales y en tiempo real |
| Costo | cuota gratuita | pago por uso, requiere alta |
| Órdenes | sin pago real; id temporal; consumen inventario copiado | reales |
| Emisión | no | tampoco, sin consolidador |

### 2.7 Lo que queda sin verificar

1. La URL base vigente del entorno de test (necesita tu dashboard).
2. Si el sandbox devuelve ofertas para rutas con origen en Bolivia (VVI, LPB, CBB). La documentación sugiere ciudades grandes, así que **podría no haber datos útiles para un caso boliviano** — es una decisión de producto que te dejo planteada en el informe.
3. Límites exactos de cuota mensual y TPS en producción.
4. Si `ticketingAgreement: DELAY_TO_CANCEL` se comporta igual en test que en producción (es nuestra red de seguridad para que una reserva huérfana se cancele sola).

---

## 3. Arquitectura propuesta

### Una interfaz, dos implementaciones, cero DTOs de Amadeus en el dominio

Mismo patrón que ya probó bien con `IAiModelClient` (ver [`ai-rag-architecture.md`](ai-rag-architecture.md)): el dominio habla un lenguaje propio y el proveedor se elige por configuración.

```
Modules/Flights/
  Services/
    IFlightProvider.cs          <- el contrato del dominio
    FlightSearchCriteria.cs     <- modelos propios, sin una sola palabra de Amadeus
    FlightQuoteResult.cs
    FlightBookingRequest.cs
    Providers/
      FakeFlightProvider.cs     <- determinístico, para tests, demo y Azure
      AmadeusFlightProvider.cs  <- HTTP + OAuth2 + mapeo
      Amadeus/                  <- DTOs de Amadeus, encapsulados acá adentro
```

```csharp
public interface IFlightProvider
{
    Task<IReadOnlyList<FlightQuote>> SearchAsync(FlightSearchCriteria criteria, CancellationToken ct);
    Task<FlightQuote> RevalidateAsync(string providerOfferRef, CancellationToken ct);
    Task<FlightBookingResult> BookAsync(FlightBookingRequest request, CancellationToken ct);
    Task<FlightBookingResult?> GetBookingAsync(string providerOrderId, CancellationToken ct);
}
```

`GetBookingAsync` no es decorativo: es lo que permite reconciliar una orden que quedó huérfana (§4.4).

Tres decisiones que vale la pena explicitar:

1. **La carga cruda del proveedor se guarda, pero no se expone.** `FlightQuote.ProviderPayload` (jsonb) guarda la oferta tal cual vino, porque Flight Offers Price y Flight Create Orders exigen reenviar **el objeto de oferta completo**, no un id. Es un detalle de integración que obliga a persistirlo; no se filtra al cliente ni al LLM.
2. **El cliente nunca manda una oferta.** Manda el id de nuestra `FlightQuote`. Si el payload viajara al navegador y volviera, cualquiera podría cambiar el precio.
3. **Selección por configuración**, igual que la IA: `Flights__Provider = Fake | Amadeus`. Azure se queda en `Fake` hasta que exista una decisión explícita de ir a producción.

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

    public string Provider { get; set; }             // "Amadeus" | "Fake"
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

| Método | Ruta | Para quién |
|---|---|---|
| `PUT` | `/api/packages/{id}/flight-rule` | Provider: configurar el vuelo |
| `DELETE` | `/api/packages/{id}/flight-rule` | Provider: dejar de incluirlo |
| `GET` | `/api/packages/{id}/flight-quotes?availabilityId&origin&travelers` | Turista: cotizar (crea `FlightQuote`) |
| `POST` | `/api/flight-quotes/{id}/revalidate` | Turista: precio confirmado antes de pagar |
| `GET` | `/api/flight-quotes/{id}/booking-requirements` | Qué datos pedirle a cada pasajero |
| `POST` | `/api/reservations` (extendido) | Suma `flightQuoteId` + `travelers[]`, con `Idempotency-Key` |
| `GET` | `/api/reservations/{id}` (extendido) | Devuelve el bloque de vuelo con su estado y localizador |

Ningún endpoint existente cambia de forma: `CreateReservationRequest` suma campos **opcionales**, y su validación actual (exactamente uno entre experiencia y paquete) se mantiene.

---

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

**Orden propuesto:**

1. **Transacción 1** — tomar el cupo del paquete con el `UPDATE` condicional que ya existe (`ReservationBookingService.HoldAndBuildAsync`), crear la `Reservation` en `PENDING_PAYMENT` y escribir `FlightBooking` en estado `PENDING` con su `IdempotencyKey`. Commit.
2. **Fuera de transacción** — revalidar y crear la orden en el proveedor.
3. **Transacción 2** — pasar `FlightBooking` a `CONFIRMED` con el localizador, o a `FAILED` con el motivo.

Escribir la intención **antes** de llamar al proveedor es lo que hace recuperable el peor caso: si el proceso se cae entre el paso 2 y el 3, queda una fila `PENDING` con su clave, y un trabajo de reconciliación puede preguntarle al proveedor (`GetBookingAsync`) si esa orden existe, para adjuntarla o cancelarla. Sin esa fila previa, una orden creada en el proveedor sin rastro nuestro es dinero de alguien perdido en el aire.

| Falla | Qué pasa |
|---|---|
| No hay cupo en el paquete | Nada se reserva, no se llama al proveedor |
| El vuelo falla tras tomar el cupo | `FlightBooking = FAILED`, se liberan los holds (`ReleaseHoldsAsync`, ya existe) y la reserva se cancela; el turista ve por qué |
| El vuelo sale bien y nuestra escritura falla | Fila `PENDING` + reconciliación; `ticketingAgreement: DELAY_TO_CANCEL` como red de seguridad adicional |
| El turista reintenta | Misma `Idempotency-Key` → se devuelve la reserva existente, no se crea otra |
| El precio cambió entre cotizar y reservar | Se frena y se pide aceptación; nunca se cobra un precio que no se mostró |

**Concurrencia:** dos turistas sobre la misma salida compiten por el cupo con el `UPDATE` condicional existente, que ya es la autoridad. El vuelo no agrega una carrera nueva: cada uno reserva su propia oferta, y si el inventario aéreo se agotó, el proveedor rechaza y cae en el caso "el vuelo falla tras tomar el cupo".

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
| `Flights__Provider` | `Fake`, o `Amadeus` cuando haya credenciales | **`Fake`** |
| Credenciales | user-secrets | ninguna hasta decisión explícita |
| Datos de pasajero | sintéticos, validados | sintéticos |

El proveedor falso no es un mock pobre: devuelve itinerarios verosímiles y determinísticos, simula cambio de precio al revalidar, vencimiento de oferta y fallo de reserva. Es lo que permite demostrar y testear los caminos difíciles —que son los que importan— sin depender de un sandbox que hoy ni siquiera resuelve.

---

## 12. Estrategia de testing

1. **Unitarios del dominio**, sin red: cálculo del precio total, vencimiento, transiciones de `FlightBookingStatus`, derivación de fechas desde la salida del paquete.
2. **Orquestación con `FakeFlightProvider`**: el camino feliz y los cuatro fallos de la tabla de §9, incluida la reserva que sobrevive a la caída entre el paso 2 y el 3.
3. **Contrato del adapter contra fixtures grabadas**: respuestas reales del sandbox, capturadas una vez en el spike, guardadas como JSON y reproducidas con un `HttpMessageHandler` falso. Verifican el mapeo sin tocar la red.
4. **CI nunca llama a Amadeus.** Ni con credenciales. Un test que depende de un tercero no es un test.
5. **Un spike manual, documentado y reproducible**, para capturar esas fixtures (fase 0).
6. Los 505 tests de backend y los 333 de frontend siguen verdes: esta integración no toca sus caminos.

---

## 13. Plan de implementación por fases

| Fase | Qué incluye | Depende de |
|---|---|---|
| **0 — Spike** | Confirmar la URL base vigente del sandbox, obtener token, correr Search → Price → Create Order a mano, **capturar las fixtures** y anotar qué devuelve para rutas bolivianas | Vos: dashboard y credenciales de test |
| **1 — Dominio + Fake** | Entidades, migración aditiva, `IFlightProvider`, `FakeFlightProvider`, endpoints de cotización, UI del proveedor para configurar el vuelo. Sin Amadeus | — |
| **2 — Adapter Amadeus** | OAuth2, mapeo, resiliencia, tests de contrato contra las fixtures. Detrás del flag, apagado por defecto | Fase 0 y 1 |
| **3 — Reserva** | Orquestación de §9, idempotencia, reconciliación, UI de pasajeros guiada por `bookingRequirements`, confirmación | Fase 2 |
| **4 — IA** | El vuelo como herramienta del agente (§14) | Fase 3 |

Cada fase termina con tests verdes y puede quedar en `master` sin activar nada: el flag apagado deja el comportamiento actual intacto.

---

## 14. Cómo encaja con el agente de IA

La regla no cambia: **el modelo no inventa nada**. Lo que cambia es de dónde salen los datos.

- El catálogo sigue viniendo del RAG estructurado sobre PostgreSQL ([`ai-rag-architecture.md`](ai-rag-architecture.md)).
- Los vuelos **no son documentos recuperables**: son el resultado de una herramienta. El agente pide una cotización, el backend llama a `IFlightProvider`, y lo que vuelve entra al prompt como contexto de candidatos, con su id.
- El modelo solo puede referirse a una `FlightQuote` que nosotros creamos. Igual que hoy con los productos, una cotización que el modelo "nombre" y no exista en el conjunto ofrecido se descarta en la capa de validación (mismo lugar donde hoy se filtran los ids inventados).
- Un itinerario de IA podría llevar un vuelo asociado con el mismo criterio que la reserva: referencia a `FlightQuote`, no un ítem más.
- El precio del vuelo en un itinerario es **estimado** por definición, y se revalida al reservar — que es exactamente lo que ya hace `ItineraryRevalidationService` con el resto.
