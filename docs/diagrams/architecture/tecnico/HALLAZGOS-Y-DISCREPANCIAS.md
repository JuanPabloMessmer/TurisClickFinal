# Hallazgos de arquitectura y discrepancias documentación ↔ código

Relevados al construir los diagramas de `docs/diagrams/architecture/` (13 de octubre de 2026, commit base
`3a17577`). Cada punto indica **dónde** se verificó. Ningún punto se corrigió en el código ni en los capítulos
existentes: este documento sólo los registra.

Fuente de verdad usada, en este orden: código de `src/TurisClick.Api` → snapshot de EF Core
(`TurisClickDbContextModelSnapshot.cs`) → `infra/terraform` y `.github/workflows` → documentos de `docs/`.

---

## 1. Estado de Azure V2 (verificado, sólo lectura)

Solicitudes `GET` anónimas del **2026-10-14 00:49 UTC** (no se ejecutó ningún comando de Azure, no se tocó la
base, no se autenticó nada):

| Solicitud | Resultado |
|---|---|
| `GET https://app-turisclick-v2-api.azurewebsites.net/api/categories` | 200 |
| `GET …/api/destinations` | 200 (incluye `imageUrl`, columna de la migración 0009) |
| `GET …/api/experiences` | 200 |
| `GET …/api/packages` | **500** — `42703: column p.cancellation_policy does not exist` |
| `GET https://ashy-rock-0dd3b480f.2.azurestaticapps.net/` | 200 (`<title>TurisClick Backoffice`) |

**Conclusión:** el incidente descrito en `docs/deployment-readiness.md` (7–8 de octubre) **sigue vigente**:
`turisclick_db_v2` no tiene aplicada la migración `0012` (y, por ser un prefijo, tampoco `0013`–`0014`). Los
diagramas `01` y `05a` muestran el App Service y la base V2 como **desplegados y degradados**. No se probó
`POST /api/auth/login` (se evitó enviar credenciales, incluso inválidas); según la documentación también
responde 500 por `users.must_change_password`.

No verificado en esta sesión (se muestra como "según la documentación"): Key Vault y sus secretos, las
identidades administradas, el state de Terraform, la existencia de un APK construido.

---

## 2. Hallazgos de arquitectura (código)

| # | Hallazgo | Evidencia | Diagrama |
|---|---|---|---|
| A1 | **Dependencia circular entre módulos**: `Reservations` usa `IFlightBookingOrchestrator` e `IFlightProvider`; `Flights` usa `IReservationBookingService` e `IPaymentGateway`. Funciona porque ningún constructor depende de sí mismo, pero acopla ambos módulos. | constructores de `ReservationService`, `ReservationCancellationService`, `FlightReconciliationService` | 02c, 03d |
| A2 | **El patrón Controller → Service → Repository se cumple parcialmente.** Flights, Preferences y Admin no tienen repositorios; en casi todos los demás módulos algún Service consulta `DbSet`s directamente. Sólo Companies y Categories cumplen la regla "el Service nunca consulta el DbContext". | búsqueda de `db.<DbSet>` en `Modules/*/Services` | 03a |
| A3 | **Tres referencias lógicas sin FK**: `payment_transactions.reservation_item_id`, `reservation_cancellation_lines.reservation_item_id` y `flight_quotes.tourist_id`. La integridad depende sólo del código. | `PaymentConfigurations.cs`, `FlightConfigurations.cs`, snapshot | 04a, 04e, 04f |
| A4 | **Los estados se guardan como `varchar` sin CHECK de dominio.** Ninguna columna `status`, `role` o `type` tiene una restricción que limite sus valores: la base aceptaría un estado inexistente. Sólo `package_items.kind` y los `product_type` quedan acotados indirectamente por los CHECK de "forma" (`ck_package_items_kind_shape`, `ck_*_product_shape`). | snapshot (41 `HasCheckConstraint`, ninguna sobre estados) | ERD (leyenda de dominios) |
| A5 | **`PAYMENT_FAILED` es un estado muerto**: existe en `ReservationStatus` y ningún código lo escribe (ya registrado como deuda en `backend-architecture.md`). | `ReservationStatus.cs`, búsqueda de usos | 07e |
| A6 | **El 500 expone el error de PostgreSQL en producción**: `GlobalExceptionHandler` pone `exception.Message` en `detail`; la respuesta pública de Azure muestra `42703: column p.cancellation_policy does not exist`. Ya listado como deuda; aquí queda **confirmado contra el entorno desplegado**. | respuesta del §1 | 03f |
| A7 | **Los itinerarios de IA no incluyen vuelos.** `AiItineraryBookingService` no referencia el módulo Flights; la integración está diseñada pero pendiente (`flight-integration-design.md` §14). | `AiItineraryBookingService.cs` | 06a, 06c |
| A8 | **Procesos en segundo plano dependientes de Always On**: los tres `BackgroundService` corren dentro del proceso web; con el plan F1 (sin Always On) no corren mientras la app duerme. Mitigación existente: `POST /api/admin/cancellations/resolve`. | `infra/terraform/locals.tf` (`always_on`), `deployment-readiness.md` §5 | 03g, 05a |
| A9 | **Deriva de configuración Terraform ↔ Azure (según documentación)**: `dev.tfvars` declara sólo dos orígenes CORS (localhost), pero `deployment-readiness.md` registra un tercer origen (el Static Web App) en el App Service. Un `terraform apply` lo eliminaría y rompería el Backoffice desplegado. | `infra/terraform/dev.tfvars`, `deployment-readiness.md` §3 | 05a |
| A10 | **El Static Web App está fuera del state de Terraform** (creado con `az staticwebapp create`), igual que la identidad de deploy de GitHub. | `infra/README.md` | 05a |

---

## 3. Discrepancias entre la documentación y la implementación

| # | Documento | Lo que dice | Lo que hay en el código | Severidad |
|---|---|---|---|---|
| D1 | `backend-architecture.md` (estructura de carpetas) | `Infrastructure/Database/Interceptors/` e `Infrastructure/Logging/SerilogConfigurator.cs` | No existen. Serilog se configura en línea en `Program.cs`; `Infrastructure/` sólo tiene `Database/` y `Security/`. | Baja |
| D2 | `backend-architecture.md` §10 | Enrichers `CorrelationId`, `UserId`/`Role` | Sólo `Enrich.FromLogContext()`; no hay middleware de CorrelationId. | Baja |
| D3 | `backend-architecture.md` §1–2 | "un Repository … nunca DbContext inline en el Service" | Ver A2: la regla se cumple en 2 de 11 módulos. | Media (afecta la descripción de la arquitectura en la tesis) |
| D4 | `backend-architecture.md` §6 | Tres políticas `RequireAdmin/Provider/Tourist` | Cuatro (`RequireProviderOrAdmin`) y todas exigen además `PasswordChangeNotPendingRequirement`. | Baja |
| D5 | `backend-architecture.md` §11 y `database-design.md` ("Convenciones", "Tipos ENUM") | Enums como **tipos `ENUM` nativos** de PostgreSQL (`HasPostgresEnum`, `CREATE TYPE …`) | `varchar(20)`/`varchar(40)` con `HasConversion<string>`; no hay `HasPostgresEnum` ni `CREATE TYPE` en las migraciones. | **Alta** para un capítulo de base de datos |
| D6 | `database-design.md` (Tipos ENUM) | `reservation_status` sin `CANCELLING`; `reservation_item_status` sin `EXPIRED` | Ambos valores existen en el código. | Media |
| D7 | `database-design.md` (bloque DBML) | 21 tablas | El esquema tiene **29**: faltan `tourist_preferences`, `tourist_preference_categories`, `package_flight_rules`, `flight_quotes`, `flight_bookings`, `payment_transactions`, `reservation_cancellations`, `reservation_cancellation_lines` (las adendas de Oleadas 10–12 describen parte de ellas en texto). Usar `04-base-de-datos/turisclick-v2.dbml`, generado del snapshot. | Media |
| D8 | `backend-architecture.md` (encabezado) | "Azure (futuro), RAG (futuro)" | Ambos existen (Azure V2 desplegado; RAG estructurado implementado). | Baja |
| D9 | `ai-rag-architecture.md` §2 (diagrama) | Nodo `AiController` | Los controllers son `AiConversationsController` y `AiItinerariesController`. | Baja |
| D10 | `ai-rag-architecture.md` §6 y `flight-integration-design.md` | "505 tests de backend" | `deployment-readiness.md` reporta 651. Cifra desactualizada en dos documentos. | Baja |
| D11 | `infra/README.md` (orden de implementación, paso 8) | Migraciones "✅ 8/8" | Hay 14 migraciones; en Azure faltan al menos 0012–0014 (§1). | **Alta** (estado operativo) |
| D12 | `CompanyOwnershipGuard.cs` (comentario XML) | "todavía no hay módulos que la invoquen" | La usan Experiences, Packages, Reservations y Flights. | Baja |
| D13 | `payments-and-cancellation.md` §6 | El ADMIN "no cambia estados" | Existe `POST /api/admin/cancellations/resolve` (reintenta la resolución; no edita estados a mano). Matiz más que contradicción: `deployment-readiness.md` §5 ya lo explica. | Baja |

---

## 4. Lo que los diagramas NO muestran a propósito

- **Ninguna base vectorial, embeddings, `pgvector`, fine-tuning ni pipeline de entrenamiento**: no existen en el
  código (búsqueda en `src/`, confirmada por `ai-rag-architecture.md` §1).
- **Ninguna pasarela de pago real ni datos de tarjeta**: `IPaymentGateway` tiene una sola implementación,
  `SimulatedPaymentGateway`.
- **Ningún repositorio genérico, capa Domain/Application ni mensajería** (colas, eventos de integración).
- **Duffel en Azure**: no está configurado (`Flights:Provider` ausente → `FakeFlightProvider`).
- **Ollama en Azure**: no existe (`Ai__Provider = Deterministic` en `infra/terraform/locals.tf`).
