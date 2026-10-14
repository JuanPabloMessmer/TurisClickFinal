# Diagramas UML de TurisClick

Diagramas UML del sistema **tal como está implementado** en `master` (commit `3a17577`), elaborados a partir
del código del backend (`src/TurisClick.Api`), de los clientes (`frontend/apps/tourist-mobile`,
`frontend/apps/backoffice`) y de la documentación en `docs/`. Cuando la documentación y el código no
coinciden, **manda el código**; las diferencias encontradas se listan al final.

Las fuentes son archivos PlantUML (`.puml`) editables y versionados. `render/` contiene las vistas previas
generadas (PNG para insertar en la tesis, SVG para escalar sin pérdida).

## Convenciones

| Convención | Significado |
|---|---|
| `<<futuro>>` (gris) | Trabajo futuro / backlog: **no implementado** |
| `<<sistema>>` (verde) | Caso de uso interno, invocado por otros (UC-SYS, agente IA) |
| `<<externo>>` | Sistema externo (Duffel, pasarela simulada, Ollama) o clase de otro agregado mostrada sólo como referencia |
| `{snapshot}` | Valor copiado y congelado al reservar o proponer; no es una referencia viva |
| `[0..1]` | Atributo opcional (nullable) |
| `/Atributo` | Atributo derivado (calculado, no persistido) |
| Códigos `UC-…` | Identificadores de `docs/use-cases.md` |

Todos los diagramas incluyen `_estilo.iuml` (paleta sobria apta para impresión).

## Índice

### Casos de uso (`casos-de-uso/`)

| Archivo | Contenido |
|---|---|
| `cu-01a-turista-cuenta-y-catalogo.puml` | Turista (1/3): registro, sesión, preferencias y catálogo público (actor Visitante) |
| `cu-01b-turista-reservas-y-pagos.puml` | Turista (2/3): reservas, pago simulado, paquete + vuelo, cancelación con reembolso |
| `cu-01c-turista-asistente-ia.puml` | Turista (3/3): asistente de viaje, itinerarios y su reserva |
| `cu-02-proveedor.puml` | Proveedor: empresa, experiencias, paquetes, salidas, regla de vuelo, política, reservas recibidas |
| `cu-03-administrador.puml` | Administrador: alta de operadores, empresas, catálogos maestros, moderación, supervisión |
| `cu-04-procesos-del-sistema.puml` | Vista complementaria: procesos en segundo plano (expiración, reconciliación, resolución) |

### Modelo de dominio (`clases/`)

| Archivo | Contenido |
|---|---|
| `clases-00-vista-general.puml` | Todos los agregados y sus asociaciones, sin atributos |
| `clases-01-identidad-y-empresas.puml` | `User`, `RefreshToken`, `Company`, `TouristPreference` |
| `clases-02-catalogo.puml` | `Destination`, `Category`, `Experience`, `Package`, ítems, imágenes, disponibilidades, `PackageFlightRule` |
| `clases-03-reservas-pagos-vuelos.puml` | `Reservation`, `ReservationItem`, `PaymentTransaction`, `ReservationCancellation(+Line)`, `FlightQuote`, `FlightBooking` |
| `clases-04-inteligencia-artificial.puml` | `AiConversation`, `AiMessage`, `AiItinerary`, `AiItineraryItem` |

### Secuencia (`secuencia/`)

| Archivo | Flujo |
|---|---|
| `sec-01a-registro-e-inicio-de-sesion.puml` | Registro e inicio de sesión del turista (UC-AUTH-01/02) |
| `sec-01b-autorizacion-renovacion-y-cierre.puml` | Autorización por rol, renovación transparente con rotación y cierre de sesión (UC-AUTH-03/04) |
| `sec-02-alta-de-proveedor-por-admin.puml` | Alta de empresa y cuenta por el ADMIN, contraseña temporal y primer ingreso (UC-P-01) |
| `sec-03-reserva-de-experiencia.puml` | Reserva con retención atómica de cupo y pago simulado (UC-T-08, UC-T-19) |
| `sec-04a-paquete-con-vuelo-cotizacion.puml` | Paquete + vuelo (1/2): cotización y revalidación con Duffel |
| `sec-04b-paquete-con-vuelo-reserva-y-pago.puml` | Paquete + vuelo (2/2): reserva coordinada, cobro y emisión del pasaje (UC-T-21) |
| `sec-05-recomendacion-ia-rag.puml` | Recomendación con IA: interpretación, retrieval SQL, composición y validación (UC-AI-01..04) |
| `sec-06a-cancelacion-presupuesto.puml` | Cancelación con reembolso (1/2): presupuesto por componente (UC-T-22) |
| `sec-06b-cancelacion-confirmacion-y-reembolso.puml` | Cancelación con reembolso (2/2): aceptación, aerolínea, transacción local y reembolsos |

### Actividad (`actividad/`)

| Archivo | Flujo |
|---|---|
| `act-01-reserva-de-paquete.puml` | Reserva de un paquete, con o sin vuelo, hasta su confirmación |
| `act-02-gestion-de-paquetes-proveedor.puml` | Ciclo de vida de un paquete gestionado por el proveedor |

## Cómo regenerar las vistas previas

Requiere Java 11+ y `plantuml.jar` (se usó **PlantUML 1.2024.7**). No hace falta Graphviz: los diagramas de
casos de uso y de clases declaran `!pragma layout smetana` (motor de layout incluido en PlantUML).

```bash
cd docs/diagrams/uml
java -jar plantuml.jar -charset UTF-8 -checkonly */*.puml
java -jar plantuml.jar -charset UTF-8 -tpng -o ../render/png */*.puml
java -jar plantuml.jar -charset UTF-8 -tsvg -o ../render/svg */*.puml
```

## Implementado vs. trabajo futuro

Implementado y modelado: autenticación JWT con rotación de refresh token; alta de operadores por ADMIN con
contraseña temporal obligatoria de cambiar; catálogo con aislamiento por empresa; reservas con retención
atómica de cupo y expiración automática; pago **simulado** con libro inmutable de movimientos; paquete + vuelo
contra Duffel **en modo de prueba** con reconciliación; cancelación con reembolso por componente; asistente IA
con retrieval estructurado sobre PostgreSQL y respaldo determinístico; moderación y sanciones.

Marcado como `<<futuro>>` (no existe en el código): pasarela de pago real, reseñas, favoritos,
notificaciones (push/correo, incluido el envío de credenciales), varios usuarios por empresa (UC-P-03),
política de cancelación en experiencias, conciliación contable y búsqueda semántica/vectorial (UC-SYS-09 no
requiere implementación con el diseño actual).

## Discrepancias encontradas entre documentación y código

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
