# Diagramas de arquitectura, infraestructura y base de datos — TurisClick V2

Diagramas técnicos de la tesis (Ingeniería de Sistemas Computacionales, UPB): arquitectura general, C4,
backend, modelo de datos, despliegue en Azure, asistente de IA y vuelos/pagos. Los diagramas UML (casos de uso,
clases de dominio, etc.) viven aparte, en `docs/diagrams/uml/`.

**Principio:** cada diagrama representa el sistema **implementado** en el commit `3a17577` (rama `master`), no el
diseño previsto. Donde la documentación y el código difieren, manda el código; las diferencias están en
[HALLAZGOS-Y-DISCREPANCIAS.md](HALLAZGOS-Y-DISCREPANCIAS.md).

## Convenciones de estado

Los diagramas distinguen explícitamente:

| Rótulo / color | Significado |
|---|---|
| verde | implementado y probado localmente (tests de integración contra PostgreSQL) |
| azul | desplegado en Azure y respondiendo (verificado) |
| rojo | desplegado en Azure, **degradado** (verificado) |
| naranja · "SIMULADO" | no mueve dinero o usa inventario de prueba (`SimulatedPaymentGateway`, `FakeFlightProvider`, Duffel TEST) |
| gris · "sólo local" | existe sólo en desarrollo (Ollama + Qwen) |
| amarillo · "según la documentación" | recurso de Azure documentado pero no verificado en esta sesión |

### Verificación de Azure

El 2026-10-14 a las 00:49 UTC se hicieron sólo `GET` anónimos (sin credenciales ni comandos de Azure):
`/api/categories`, `/api/destinations` y `/api/experiences` → 200; **`/api/packages` → 500**
(`column p.cancellation_policy does not exist`); Backoffice en Static Web Apps → 200. Es decir, **Azure V2 no
está sano**: a `turisclick_db_v2` le faltan las migraciones 0012–0014. Detalle en
[HALLAZGOS-Y-DISCREPANCIAS.md §1](HALLAZGOS-Y-DISCREPANCIAS.md#1-estado-de-azure-v2-verificado-sólo-lectura).

---

## Índice

Cada diagrama tiene su fuente editable y dos vistas previas (`.svg` vectorial para el documento, `.png` a 2×).

### A. Arquitectura general

| Diagrama | Fuente | Propósito | Evidencia en el código |
|---|---|---|---|
| Arquitectura general y estado | [01-arquitectura-general.puml](01-general/01-arquitectura-general.puml) | Clientes, API modular, autenticación, IA, vuelos, pago simulado, PostgreSQL y Azure, con el estado real de cada pieza | `Program.cs`, `Modules/*/…ModuleExtensions.cs`, `frontend/apps/*/package.json`, `infra/terraform/*` |

### B. C4 (C4-PlantUML)

| Diagrama | Fuente | Propósito | Evidencia |
|---|---|---|---|
| Nivel 1 — Contexto | [02a-c4-n1-contexto.puml](02-c4/02a-c4-n1-contexto.puml) | Actores (TOURIST, PROVIDER, ADMIN) y sistemas externos (Duffel, Ollama, Wikimedia Commons) | `Program.cs` (políticas por rol), `FlightsModuleExtensions.cs`, `AiModuleExtensions.cs`, `tools/demo-catalog/images.manifest.json` |
| Nivel 2 — Contenedores | [02b-c4-n2-contenedores.puml](02-c4/02b-c4-n2-contenedores.puml) | Tourist Mobile, Backoffice, API, PostgreSQL; tecnologías y protocolos | `TurisClick.Api.csproj`, `frontend/apps/*`, `frontend/packages/*`, `*/src/lib/env.ts` |
| Nivel 3 — Componentes de la API | [02c-c4-n3-componentes-api.puml](02-c4/02c-c4-n3-componentes-api.puml) | Módulos como componentes y sus dependencias **reales** de constructor | constructores de cada Service/Repository |

### C. Backend

| Diagrama | Fuente | Propósito | Evidencia |
|---|---|---|---|
| Patrón por módulo | [03a-patron-por-modulo.puml](03-backend/03a-patron-por-modulo.puml) | Estructura Controller → Service → Repository → DbContext y tabla de cumplimiento por módulo | `Modules/Auth/**`, búsqueda de `db.<DbSet>` en Services |
| Identidad y administración | [03b-modulos-identidad-administracion.puml](03-backend/03b-modulos-identidad-administracion.puml) | Auth, Companies, Preferences, Admin | `Modules/{Auth,Companies,Preferences,Admin}/**` |
| Catálogo | [03c-modulos-catalogo.puml](03-backend/03c-modulos-catalogo.puml) | Destinations, Categories, Experiences, Packages | `Modules/{Destinations,Categories,Experiences,Packages}/**` |
| Reservas, pagos y vuelos | [03d-modulos-reservas-pagos-vuelos.puml](03-backend/03d-modulos-reservas-pagos-vuelos.puml) | Servicios, interfaces y el ciclo Reservations ↔ Flights | `Modules/Reservations/**`, `Modules/Flights/**` |
| Módulo de IA | [03e-modulo-ia.puml](03-backend/03e-modulo-ia.puml) | Agente, retrieval, repositorios y composición de `IAiModelClient` por configuración | `Modules/Ai/**`, `AiModuleExtensions.cs` |
| Pipeline HTTP y seguridad | [03f-pipeline-http-seguridad.puml](03-backend/03f-pipeline-http-seguridad.puml) | Orden real del middleware, JWT, políticas, guard de empresa, mapeo de excepciones | `Program.cs`, `Infrastructure/Security/*`, `GlobalExceptionHandler.cs` |
| Procesos en segundo plano | [03g-procesos-segundo-plano.mmd](03-backend/03g-procesos-segundo-plano.mmd) | Los tres `BackgroundService`, intervalos, lotes y reintentos | `*BackgroundService.cs`, `*Options` |

### D. Base de datos (ERD)

Generados **automáticamente** desde el snapshot de EF Core por
[`_herramientas/generar_erd.py`](_herramientas/generar_erd.py): tablas, columnas, tipos, nulabilidad, PK, FK,
acciones `ON DELETE`, índices únicos (incluidos los parciales) y CHECK salen del modelo; no hay transcripción a
mano. Notación de Ingeniería de la Información (pata de gallo). Las entidades grises son tablas de otra vista,
con sólo su PK.

| Diagrama | Fuente | Contenido |
|---|---|---|
| Esquema completo (DBML) | [turisclick-v2.dbml](04-base-de-datos/turisclick-v2.dbml) | 29 tablas, 271 columnas, 49 FK, índices, CHECK y dominios de cada `varchar` de estado. Abre en dbdiagram.io |
| 1. ERD general | [04a-erd-general.puml](04-base-de-datos/04a-erd-general.puml) | Las 29 tablas agrupadas por módulo, sólo claves |
| 2. Usuarios, roles y proveedores | [04b-erd-usuarios-proveedores.puml](04-base-de-datos/04b-erd-usuarios-proveedores.puml) | `users`, `refresh_tokens`, `companies`, `tourist_preferences`, `tourist_preference_categories` |
| 3. Destinos, categorías y catálogo | [04c-erd-destinos-categorias.puml](04-base-de-datos/04c-erd-destinos-categorias.puml) | `destinations` (jerarquía), `categories`, tablas de clasificación |
| 4. Experiencias, paquetes y disponibilidad | [04d-erd-experiencias-paquetes.puml](04-base-de-datos/04d-erd-experiencias-paquetes.puml) | Productos, imágenes, ítems de paquete y cupos |
| 5. Reservas, pagos y reembolsos | [04e-erd-reservas-pagos.puml](04-base-de-datos/04e-erd-reservas-pagos.puml) | `reservations`, `reservation_items`, `payment_transactions`, cancelaciones y sus líneas |
| 6. Vuelos | [04f-erd-vuelos.puml](04-base-de-datos/04f-erd-vuelos.puml) | `package_flight_rules`, `flight_quotes`, `flight_bookings` |
| 7. Persistencia de la IA | [04g-erd-ia-itinerarios.puml](04-base-de-datos/04g-erd-ia-itinerarios.puml) | Conversaciones, mensajes, itinerarios versionados e ítems |

Evidencia: `src/TurisClick.Api/Infrastructure/Database/Migrations/TurisClickDbContextModelSnapshot.cs`
(14 migraciones, última `0014_Oleada13_AltaDeOperadoresPorAdmin`) y las `IEntityTypeConfiguration` de cada
`Modules/*/Persistence/`. Las tres "referencias lógicas" (columnas con id de otra tabla **sin FK**) se dibujan
punteadas y se declaran a mano en el generador, que verifica que efectivamente no tengan FK.

### E. Despliegue en Azure

| Diagrama | Fuente | Propósito | Evidencia |
|---|---|---|---|
| Despliegue V2 | [05a-despliegue-azure-v2.puml](05-azure/05a-despliegue-azure-v2.puml) | App Service F1, Static Web Apps, PostgreSQL Flexible con bases V1 y V2 separadas, Key Vault, identidades administradas, GitHub Actions, APK Android, integraciones ausentes. Estado verificado | `infra/terraform/*.tf`, `dev.tfvars`, `infra/README.md`, `infra/bootstrap/*.ps1`, `.github/workflows/backend-v2.yml` |
| CI/CD y operación manual | [05b-cicd-y-operacion-manual.mmd](05-azure/05b-cicd-y-operacion-manual.mmd) | Jobs del workflow, sonda de esquema previa al deploy y pasos manuales de `infra/bootstrap` | `.github/workflows/backend-v2.yml`, `docs/azure-v2-deployment-plan.md` |

### F. IA híbrida + RAG

| Diagrama | Fuente | Propósito | Evidencia |
|---|---|---|---|
| Flujo en 8 etapas | [06a-flujo-ia-hibrida-rag.puml](06-ia-rag/06a-flujo-ia-hibrida-rag.puml) | Pedido → extracción determinística → Qwen vía Ollama → interpretación híbrida → retrieval SQL → composición → validación de precio y cupo → persistencia y reserva. Incluye la configuración real local vs. Azure | `AiConversationService.cs`, `LlmClients/*`, `RetrievalService.cs`, `AiCatalogRepository.cs`, `appsettings*.json`, `infra/terraform/locals.tf` |
| Secuencia de un turno | [06b-secuencia-turno-conversacion.mmd](06-ia-rag/06b-secuencia-turno-conversacion.mmd) | Interacciones de un mensaje con proveedor Hybrid | ídem |
| Secuencia de reserva del itinerario | [06c-secuencia-reserva-itinerario.mmd](06-ia-rag/06c-secuencia-reserva-itinerario.mmd) | Revalidación, aceptación de precio, holds atómicos e idempotencia | `AiItineraryBookingService.cs`, migración 0007 |

No se dibujan bases vectoriales, embeddings ni entrenamiento: **no existen en el código**.

### G. Vuelos y pagos

| Diagrama | Fuente | Propósito | Evidencia |
|---|---|---|---|
| Abstracción del proveedor | [07a-abstraccion-proveedor-vuelos.puml](07-vuelos-pagos/07a-abstraccion-proveedor-vuelos.puml) | `IFlightProvider`, `FakeFlightProvider` (escenarios), `DuffelFlightProvider` (TEST), excepciones | `Modules/Flights/Services/**` |
| Cotización, reserva y pago | [07b-secuencia-cotizacion-reserva-pago.mmd](07-vuelos-pagos/07b-secuencia-cotizacion-reserva-pago.mmd) | Cotizar, revalidar, reservar con vuelo, cobro simulado, libro de pagos y emisión | `PackageFlightService.cs`, `ReservationService.cs`, `FlightBookingOrchestrator.cs`, `PaymentLedger.cs` |
| Estados de `flight_bookings` | [07c-estados-flightbooking.mmd](07-vuelos-pagos/07c-estados-flightbooking.mmd) | Máquina de estados de la reserva aérea | `FlightEntities.cs` |
| Cancelación con reembolso | [07d-secuencia-cancelacion-reembolso.mmd](07-vuelos-pagos/07d-secuencia-cancelacion-reembolso.mmd) | Presupuesto, aceptación, aerolínea primero, reembolsos idempotentes | `ReservationCancellationService.cs` |
| Estados de reserva y cancelación | [07e-estados-reserva-y-cancelacion.mmd](07-vuelos-pagos/07e-estados-reserva-y-cancelacion.mmd) | `ReservationStatus` y `ReservationCancellationStatus` | `Reservations/Entities/*`, servicios |
| Reconciliación | [07f-reconciliacion-vuelos.mmd](07-vuelos-pagos/07f-reconciliacion-vuelos.mmd) | Resolución de emisiones con desenlace desconocido | `FlightReconciliationService.cs` |

---

## Validación realizada

| Control | Resultado |
|---|---|
| Sintaxis PlantUML (`-checkonly`, 20 archivos) | sin errores |
| Render PlantUML a SVG y PNG | 20/20 |
| Sintaxis Mermaid (`mermaid.parse`, 9 archivos) y render a SVG y PNG | 9/9 |
| DBML con el parser oficial (`@dbml/core`) | válido: 29 tablas, 49 relaciones, 10 grupos |
| Tablas del snapshot = tablas de las vistas | verificado por el generador (falla si difieren) |
| Valores de cada enum de la leyenda = declaración en C# | verificado por el generador (23 enums, falla si difieren) |
| Referencias lógicas realmente sin FK | verificado por el generador |
| Inspección visual | los 29 PNG se abrieron y revisaron; se corrigieron proporciones (vistas de backend demasiado anchas, flujo RAG demasiado alto), una arista fuera del lienzo (04a), glifos sin fuente y texto interpretado como formato. Los cambios posteriores que sólo tocaron texto (02a, 02c, 06a, 07a, 03g) se re-renderizaron sin volver a abrirse |
| Exactitud contra el código | cada relación, método y código HTTP de las secuencias se contrastó con el código fuente |

## Cómo regenerar

Requisitos: Java 17+, Node 20+, Python 3 y un navegador Chromium (Edge o Chrome). No hace falta Graphviz: los
`.puml` usan el motor Smetana incluido en PlantUML.

```bash
cd docs/diagrams/architecture/_herramientas
npm install
```

Descargar `plantuml-1.2025.4.jar` de Maven Central (`net.sourceforge.plantuml:plantuml:1.2025.4`) y, desde la
raíz del repositorio:

```bash
PLANTUML_JAR=/ruta/plantuml-1.2025.4.jar bash docs/diagrams/architecture/render.sh
```

`render.sh` regenera los ERD y el DBML desde el snapshot, valida el DBML y renderiza todos los PlantUML y Mermaid.
Si el navegador no es Edge en la ruta por defecto de Windows, definir `BROWSER_PATH`.

Para editar un ERD, cambiar la vista (o `ENUMS`/`LOGICAL_REFS`) en `generar_erd.py`, no el `.puml` generado.
Después de una migración nueva basta con volver a correr `render.sh`.

### Por qué un renderizador propio para Mermaid

`@mermaid-js/mermaid-cli 11.4.2` falla al cargar su página interna con `mermaid 11.17` (`loaders is not
iterable`). `render-mermaid.mjs` usa el mismo `mermaid` instalado mediante `puppeteer-core` y fija el tamaño
natural de cada SVG.
