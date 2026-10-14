# Diagramas UML técnicos (anexos)

Versión **detallada** de los diagramas, al nivel de la implementación: nombres de clases y métodos reales,
endpoints, códigos de estado, transacciones y estados internos. Se conservan como material de **anexo** o
de consulta para quien revise el código; para el cuerpo de la tesis se usan las vistas simplificadas de
[`../thesis/`](../thesis/) (ver [`../README.md`](../README.md)).

Reflejan el sistema implementado en `master` (commit `3a17577`). `render/` contiene PNG y SVG generados.

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
cd docs/diagrams/uml/technical
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

La lista verificada de diferencias entre `docs/` y el código está en [`../README.md`](../README.md#discrepancias-entre-documentación-y-código).
