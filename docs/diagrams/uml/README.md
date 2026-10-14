# Diagramas UML de TurisClick

Diagramas UML del sistema **tal como está implementado** en `master` (commit `3a17577`), verificados contra el
código del backend (`src/TurisClick.Api`), de Tourist Mobile y del Backoffice. Cuando la documentación de
`docs/` y el código no coinciden, manda el código (ver [discrepancias](#discrepancias-entre-documentación-y-código)).

| Carpeta | Para qué |
|---|---|
| [`thesis/`](thesis/) | **Diagramas para el cuerpo de la tesis.** Nivel de abstracción de análisis y diseño: objetivos de usuario, conceptos del dominio y flujos de negocio. Sin identificadores internos, SQL, códigos HTTP ni nombres de métodos. |
| [`technical/`](technical/) | **Diagramas detallados para anexos.** Nivel de implementación: clases y métodos reales, endpoints, estados internos, transacciones y casos de falla. |

Cada carpeta contiene las fuentes PlantUML (`.puml`, editables) y `render/png` + `render/svg`.

## Selección recomendada

**Cuerpo de la tesis (9 figuras).** Cubren los tres actores, el dominio y los tres procesos que distinguen
a TurisClick: reserva con control de cupo, integración aérea y asistente de IA.

1. `cu-01-turista` · 2. `cu-02-proveedor` · 3. `cu-03-administrador`
4. `cd-01-modelo-conceptual`
5. `sd-03-reserva-de-experiencia` · 6. `sd-04-reserva-de-paquete-con-vuelo` · 7. `sd-05-recomendacion-con-ia`
8. `ad-01-reserva-de-paquete` · 9. `ad-02-gestion-de-paquetes`

**Cuerpo o anexo, según la extensión del capítulo:** `cd-02` a `cd-04` (diseño detallado de clases),
`sd-01` (autenticación), `sd-02` (alta de proveedores) y `sd-06` (cancelación con reembolso).

**Anexos técnicos (`technical/`)** que aportan valor propio:

| Diagrama técnico | Qué agrega frente a la versión de tesis |
|---|---|
| `sec-01b-autorizacion-renovacion-y-cierre` | Políticas por rol, renovación transparente con rotación de tokens y cierre de sesión |
| `sec-04b-paquete-con-vuelo-reserva-y-pago` | Idempotencia, transición a ORDERING antes de la llamada externa y los cuatro desenlaces de la emisión |
| `sec-06b-cancelacion-confirmacion-y-reembolso` | Orden de la orquestación entre base de datos, aerolínea y pasarela, y qué ocurre ante cada falla |
| `sec-05-recomendacion-ia-rag` | Capas de defensa contra alucinaciones y respaldo determinístico del modelo |
| `clases-03-reservas-pagos-vuelos` | Atributos completos, enumeraciones de estados e invariantes de reserva y pago |
| `cu-04-procesos-del-sistema` | Procesos automáticos en segundo plano (expiración, reconciliación, resolución) |

## Descripción de las figuras (para leyendas)

### Casos de uso

**`cu-01-turista` — Casos de uso del Turista.** Muestra los objetivos que el turista alcanza desde la
aplicación móvil: gestionar su cuenta, explorar la oferta, planificar con el asistente de IA, reservar,
pagar, consultar y cancelar. La inclusión del vuelo extiende la reserva sólo cuando el paquete lo incluye;
el vuelo se cotiza al elegir la salida y el pasaje se emite al pagar. La pasarela de pago es simulada y el
proveedor aéreo opera en modo de prueba.

**`cu-02-proveedor` — Casos de uso del Proveedor.** Resume cómo un operador turístico administra su oferta
desde el Backoffice: perfil de empresa, experiencias, paquetes con su política de cancelación, regla de
vuelo y disponibilidad con cupos. También consulta las reservas que corresponden a su empresa y, de forma
excepcional, cancela su línea de una reserva. Todas las operaciones se limitan a los datos de su propia empresa.

**`cu-03-administrador` — Casos de uso del Administrador.** Presenta las responsabilidades de gobierno de la
plataforma: alta de empresas operadoras con su primera cuenta, revisión y sanción de empresas, catálogos
maestros, moderación de usuarios y contenido, y supervisión de la operación y de los pagos. Las sanciones
actúan como filtro de visibilidad y operación sin borrar información.

### Modelo de dominio

**`cd-01-modelo-conceptual` — Modelo conceptual del dominio.** Identifica los conceptos principales y sus
relaciones: las empresas ofrecen productos turísticos ubicados en destinos, cada producto publica
disponibilidades con cupo, y una reserva agrupa líneas que retienen cupo, sus movimientos de pago y, si
corresponde, una reserva aérea. "Producto turístico" es una abstracción conceptual que agrupa experiencias
y paquetes (ver la correspondencia con la implementación más abajo).

**`cd-02-detalle-catalogo` — Diseño detallado del catálogo.** Refina el catálogo con las clases reales y sus
atributos clave: jerarquía de destinos, categorías, experiencias, paquetes compuestos por ítems, turnos,
salidas y la regla de búsqueda de vuelos de un paquete. Un ítem de paquete referencia una experiencia de la
misma empresa o es puramente descriptivo.

**`cd-03-detalle-reservas-pagos` — Diseño detallado de reservas y pagos.** Muestra cómo cada línea de reserva
congela precio y política de cancelación y retiene cupo sobre un turno o salida. El dinero se registra en un
libro inmutable de movimientos, las cancelaciones se presupuestan por componente y el vuelo se modela como
reserva aérea independiente, originada en una cotización.

**`cd-04-detalle-asistente-ia` — Diseño detallado del asistente de IA.** Describe la conversación con sus
preferencias interpretadas, el historial de mensajes y los itinerarios versionados. Cada ítem del itinerario
referencia un producto real del catálogo con un precio estimado congelado; un itinerario aceptado origina
una reserva.

### Secuencia

**`sd-01-registro-e-inicio-de-sesion` — Registro e inicio de sesión del turista.** El servicio de
autenticación verifica que el correo no exista, cifra la contraseña y emite un token de acceso de corta
duración junto con un token de renovación persistido. La aplicación guarda este último en almacenamiento
seguro para renovar la sesión sin volver a pedir credenciales.

**`sd-02-alta-de-proveedor` — Alta de una empresa operadora por el administrador.** El administrador
registra la empresa y su contacto; el sistema genera una contraseña temporal que se muestra una única vez.
En su primer ingreso el proveedor sólo puede cambiar esa contraseña, y al hacerlo se cierran las demás
sesiones. Por defecto la empresa queda aprobada, aunque el administrador puede dejarla pendiente.

**`sd-03-reserva-de-experiencia` — Reserva y pago de una experiencia.** La reserva verifica la
disponibilidad, retiene el cupo de forma atómica y congela el precio durante 30 minutos. Al pagar se
revalida el precio vigente, se cobra mediante la pasarela simulada y la reserva se confirma
automáticamente, sin aprobación manual del proveedor.

**`sd-04-reserva-de-paquete-con-vuelo` — Reserva de un paquete con vuelo.** El vuelo se cotiza con el
proveedor aéreo a partir de la regla configurada por el operador, se reserva junto con la salida del
paquete y se emite recién después de revalidar el precio y aprobar el cobro. Paquete y vuelo pueden tener
monedas distintas, por lo que se cobra un cargo por moneda.

**`sd-05-recomendacion-con-ia` — Recomendación de itinerario con el asistente de IA.** El modelo de lenguaje
interpreta el pedido y redacta la propuesta, pero los productos, precios y cupos provienen siempre del
catálogo (generación aumentada por recuperación sobre consultas estructuradas). El agente valida cada
componente antes de guardar una nueva versión del itinerario.

**`sd-06-cancelacion-con-reembolso` — Cancelación de una reserva confirmada.** El turista recibe primero un
presupuesto por componente, calculado con la política congelada del paquete y lo que informa la aerolínea.
Al aceptarlo se cancela primero el pasaje, luego la reserva y el cupo, y por último se reembolsa cada
componente. El diagrama muestra el caso de una reserva con vuelo.

### Actividad

**`ad-01-reserva-de-paquete` — Proceso de reserva de un paquete.** Describe el flujo de negocio desde la
elección de la salida hasta la confirmación, con sus puntos de decisión: cupo disponible, cambio de precio,
aprobación del pago y emisión del pasaje cuando el paquete incluye vuelo. Si el pasaje no puede emitirse,
la reserva se cancela, el cupo se libera y el cobro se revierte; los desenlaces intermedios (solicitud
no enviada o resultado desconocido) se detallan en el anexo técnico.

**`ad-02-gestion-de-paquetes` — Gestión de un paquete por el proveedor.** Muestra el ciclo de vida del
paquete: creación como borrador, definición de salidas y vuelo, publicación sujeta a requisitos mínimos y
mantenimiento mientras está a la venta. Las ediciones posteriores no alteran las reservas ya realizadas.

## Trazabilidad con `docs/use-cases.md`

| Diagrama | Caso de uso del diagrama | Casos de uso documentados |
|---|---|---|
| cu-01 | Gestionar cuenta y sesión | UC-AUTH-01 a UC-AUTH-04 |
| cu-01 | Configurar preferencias de viaje | Onboarding de preferencias (`/api/tourists/me/preferences`, sin código UC) |
| cu-01 | Explorar experiencias y paquetes | UC-T-03 a UC-T-07 |
| cu-01 | Planificar viaje con el asistente de IA | UC-T-12 a UC-T-17, UC-AI-01 a UC-AI-06 |
| cu-01 | Reservar itinerario propuesto | UC-T-18 (UC-SYS-05) |
| cu-01 | Reservar experiencia o paquete | UC-T-08, UC-T-09 (UC-SYS-01, UC-SYS-04, UC-SYS-06) |
| cu-01 | Incluir vuelo en el paquete | UC-T-19 (vuelos), UC-T-20, UC-T-21 |
| cu-01 | Pagar reserva | UC-T-19 (pago), UC-SYS-02, UC-SYS-07 |
| cu-01 | Consultar mis viajes | UC-T-10 |
| cu-01 | Cancelar reserva | UC-T-11, UC-T-22 (UC-SYS-08) |
| cu-02 | Gestionar cuenta y sesión | UC-AUTH-02 a UC-AUTH-04 y cambio de contraseña temporal (UC-P-01) |
| cu-02 | Gestionar perfil de la empresa | UC-P-02 |
| cu-02 | Gestionar experiencias | UC-P-04 a UC-P-06 |
| cu-02 | Gestionar paquetes y su política de cancelación | UC-P-07 a UC-P-09, UC-P-16 |
| cu-02 | Configurar vuelo del paquete | UC-P-15 |
| cu-02 | Definir disponibilidad y cupos | UC-P-10, UC-P-11 |
| cu-02 | Consultar reservas recibidas | UC-P-12, UC-P-13 |
| cu-02 | Cancelar una línea de reserva | UC-P-14 |
| cu-03 | Gestionar cuenta y sesión | UC-AUTH-02 a UC-AUTH-04 |
| cu-03 | Dar de alta empresa operadora y su cuenta | UC-P-01 |
| cu-03 | Revisar solicitudes de empresas | UC-A-01 a UC-A-03 |
| cu-03 | Suspender o reactivar empresas | UC-A-08 |
| cu-03 | Gestionar destinos y categorías | UC-A-04, UC-A-05 |
| cu-03 | Moderar usuarios | UC-A-06 |
| cu-03 | Moderar experiencias y paquetes | UC-A-07 |
| cu-03 | Supervisar la operación de la plataforma | Panorama y listados globales, UC-A-09 |
| cu-03 | Supervisar pagos y cancelaciones | UC-A-10 y resolución manual de cancelaciones pendientes |

Las validaciones internas (verificar disponibilidad, revalidar precio, validar propiedad de la empresa) no
aparecen como casos de uso propios: son pasos de los casos que las invocan y se ven en los diagramas de
secuencia y actividad.

## Correspondencia entre el modelo conceptual y la implementación

| Concepto (cd-01) | Implementación | Diferencia relevante |
|---|---|---|
| Usuario | `User` | El rol (TOURIST, PROVIDER, ADMIN) es un atributo, no una subclase |
| Empresa operadora | `Company` | — |
| Destino | `Destination` | Jerarquía país → región → ciudad; los productos se ubican en una ciudad |
| Producto turístico | — (abstracción conceptual) | **No existe clase base**: `Experience` y `Package` son entidades independientes con atributos propios |
| Paquete *incluye* Experiencia | `PackageItem` | El ítem referencia una experiencia de la misma empresa **o** es un ítem descriptivo |
| Disponibilidad | `ExperienceAvailability` (turno) / `PackageAvailability` (salida) | Dos tablas separadas con la misma estructura de cupo |
| Reserva / Línea de reserva | `Reservation` / `ReservationItem` | La línea guarda la empresa de forma desnormalizada para aislar lo que ve cada proveedor |
| Movimiento de pago | `PaymentTransaction` | Libro inmutable: cobros, reversos y reembolsos |
| Reserva aérea | `FlightBooking` (+ `FlightQuote`) | No es una línea de reserva: no tiene empresa ni disponibilidad propia |
| Itinerario de IA | `AiItinerary` (+ `AiConversation`, `AiItineraryItem`) | Cada ajuste crea una versión nueva |

Elementos de implementación omitidos en el modelo conceptual y presentes en `cd-02` a `cd-04` o en
`technical/`: tokens de renovación, categorías, preferencias del turista, mensajes, imágenes, regla de vuelo
y cancelaciones presupuestadas.

## Alcance: implementado y trabajo futuro

Los diagramas de `thesis/` muestran **sólo funcionalidad implementada**. Quedan fuera, como trabajo futuro:
pasarela de pago real, reseñas, favoritos, notificaciones (incluido el envío de credenciales por correo),
varios usuarios por empresa, política de cancelación en experiencias y búsqueda semántica. Los diagramas de
`technical/` los muestran marcados como `<<futuro>>`.

## Cómo regenerar las vistas previas

Requiere Java 11+ y `plantuml.jar` (se usó PlantUML 1.2024.7). No hace falta Graphviz: los diagramas de casos
de uso y de clases usan el motor de layout incluido (`!pragma layout smetana`).

```bash
cd docs/diagrams/uml/thesis
java -jar plantuml.jar -charset UTF-8 -checkonly */*.puml
java -jar plantuml.jar -charset UTF-8 -tpng -o ../render/png */*.puml
java -jar plantuml.jar -charset UTF-8 -tsvg -o ../render/svg */*.puml
```

Lo mismo vale para `technical/`.

## Discrepancias entre documentación y código

Verificadas contra el código de `master`. Ninguna se corrigió en este trabajo (alcance: sólo diagramas).

| # | Documento | Qué dice | Qué hace el código |
|---|---|---|---|
| 1 | `use-cases.md`, UC-T-11 (y comentario XML de `ReservationService.CancelAsync`) | Cancelar una reserva `CONFIRMED` responde 409 `REFUND_POLICY_REQUIRED` | Desde la Oleada 12 se cancela con presupuesto aceptado (`cancellation-quote` + `cancel`); sin presupuesto responde 409 `CANCELLATION_QUOTE_REQUIRED` |
| 2 | `use-cases.md`, tabla resumen | UC-P-01 "Registrar empresa y solicitar cuenta Provider", actor PROVIDER | El autorregistro se eliminó: el alta la hace un ADMIN (`POST /api/admin/provider-accounts`); la ficha detallada sí está actualizada |
| 3 | `use-cases.md`, Oleada 10 | El código UC-T-19 se usa para "Pagar una reserva pendiente" y para "Cotizar el vuelo de un paquete" | Colisión de identificadores; en los diagramas se distingue como "UC-T-19 vuelos" |
| 4 | `use-cases.md`, Oleada 10 | Cotizar y revalidar vuelo: "Turista (público)" | Ambos endpoints exigen `RequireTourist` (sesión de turista); sólo `GET /api/airports` es público |
| 5 | `domain-model.md` | No describe `TouristPreference`, `PackageFlightRule`, `FlightQuote`, `FlightBooking`, `PaymentTransaction` ni `ReservationCancellation(+Line)`; la sección de descartes dice que `Payment` "no se modela todavía"; el diagrama ER tampoco los incluye | Todas existen como entidades EF Core (el libro de pagos cumple el rol de `Payment`) |
| 6 | `domain-model.md`, tabla de enums | `ReservationStatus` sin `CANCELLING` | El enum incluye `CANCELLING` (la sección 7 sí lo menciona) |
| 7 | `domain-model.md`, atributos | Faltan `User.MustChangePassword`, `Destination.ImageUrl/CreatedAt`, `Package.IncludesFlight/CancellationPolicy`, `ReservationItem.CancellationPolicy`, `AiConversation.TravelPace`; la moneda del presupuesto figura como `Currency` | Existen en las entidades; el campo se llama `BudgetCurrency` |
| 8 | `domain-model.md`, regla 1 | Un PROVIDER sólo puede crear **y editar** productos si su empresa está `APPROVED` | Sólo la creación verifica `APPROVED`; `UpdateAsync` de experiencias y paquetes verifica únicamente la propiedad, así que una empresa suspendida puede editar (no publicar) |
| 9 | `domain-model.md`, `Company.ApprovedByUserId` | ADMIN que aprobó o rechazó | `ApproveAsync` lo completa, pero el alta por ADMIN con `Approve = true` sólo fija `ApprovedAt` y deja `ApprovedByUserId` nulo |
| 10 | `payments-and-cancellation.md` §6 | La vista del ADMIN "no cambia estados" | Existe `POST /api/admin/cancellations/resolve`, que dispara la misma resolución idempotente del proceso de fondo |
| 11 | `payments-and-cancellation.md` / UC-P-14 | Reembolso por componente | La cancelación de una línea `CONFIRMED` por el proveedor libera cupo pero no registra reembolso en el libro ni notifica al turista (brecha funcional, no sólo documental) |
| 12 | `use-cases.md`, UC-A-02/03 y UC-P-14 | "Se notifica al Provider / al turista" | No hay infraestructura de notificaciones (UC-P-14 ya lo aclara; UC-A-02/03 no) |
| 13 | `domain-model.md`, enums | `AiItineraryStatus.DISCARDED`, `AiConversationStatus.CLOSED` como estados del ciclo de vida | Ningún flujo los asigna (igual que `PAYMENT_FAILED`, que sí está documentado como estado muerto) |
| 14 | `ai-rag-architecture.md`, diagrama de flujo | Nodo "API AiController" | Los controladores reales son `AiConversationsController` y `AiItinerariesController` |

Observación adicional del código (no contradice documentación): la cotización de vuelo
(`PackageFlightService.QuoteAsync`) no verifica si la empresa del paquete está suspendida; la reserva
posterior sí lo rechaza, así que no se puede comprar, pero se consulta al proveedor aéreo innecesariamente.
