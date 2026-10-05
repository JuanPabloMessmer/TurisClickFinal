# Vuelos en TurisClick — propuesta técnica

Estado: **abstracción y adapter implementados y probados contra una API real; dominio persistido todavía no**.
Lo que existe hoy en código: `IFlightProvider`, `DuffelFlightProvider`, `FakeFlightProvider` y el spike
que los ejercita. Lo que sigue en propuesta: las entidades, la migración y la orquestación de reserva.

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
