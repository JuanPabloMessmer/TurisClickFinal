# TurisClick — FASE 4: Arquitectura del backend

Stack: ASP.NET Core 10 Web API, PostgreSQL 16, EF Core + Npgsql, JWT, `PasswordHasher<T>` (PBKDF2 vía ASP.NET Core Identity), Swagger/OpenAPI, Serilog, Postman, Git/GitHub, Azure (futuro), RAG (futuro).

Principio rector: **arquitectura modular orientada a casos de uso** (`docs/use-cases.md`), no a capas técnicas gigantes. Cada carpeta bajo `Modules/` es prácticamente un mini-proyecto autocontenido que expone sus propios Controllers/Services/Repositories/DTOs/Entities.

---

## Estructura de carpetas

```
TurisClick.sln

src/
  TurisClick.Api/
    Modules/
      Auth/
        Controllers/        AuthController.cs
        Services/            IAuthService, AuthService, ITokenService, JwtTokenService
        Repositories/        IUserRepository, UserRepository, IRefreshTokenRepository, RefreshTokenRepository
        Dtos/                RegisterTouristRequest, LoginRequest, AuthTokensResponse, ...
        Entities/            User.cs, RefreshToken.cs
        Persistence/         UserConfiguration.cs, RefreshTokenConfiguration.cs  (IEntityTypeConfiguration<T>)
      Companies/             (mismo patrón — Oleada 1)
      Destinations/          (Oleada 1)
      Categories/            (Oleada 1)
      Experiences/           (Oleada 2)
      Packages/              (Oleada 4)
      Reservations/          (Oleada 2/7)
      Ai/                    (placeholder hasta Oleada 5+)

    Infrastructure/
      Database/
        TurisClickDbContext.cs
        Migrations/          (una carpeta, todas las migraciones del proyecto)
        Interceptors/        (ej. actualizar UpdatedAt automáticamente)
      Security/
        PasswordHasherService.cs   (envoltorio sobre PasswordHasher<User>)
        CurrentUserContext.cs      (lee claims del JWT del request actual)
        CompanyOwnershipGuard.cs   (UC-SYS-03)
      Logging/
        SerilogConfigurator.cs

    Shared/
      Exceptions/            NotFoundException, ConflictException, ForbiddenException, ValidationAppException
      Responses/              PagedResult<T>, ProblemDetailsFactory helpers
      Validation/             atributos de validación reutilizables (ej. Iso4217CurrencyAttribute)

    Program.cs
    appsettings.json
    appsettings.Development.json

tests/
  TurisClick.Api.Tests/
    Modules/Auth/            AuthServiceTests.cs, AuthControllerIntegrationTests.cs
    ...

docs/
postman/
  TurisClick.postman_collection.json
```

### Responsabilidad de cada carpeta

| Carpeta | Responsabilidad |
|---|---|
| `Modules/<Feature>/Controllers` | Recibe el HTTP request, valida el modelo (`ModelState`), llama **un solo** método del Service, traduce el resultado a `IActionResult`. No contiene lógica de negocio. |
| `Modules/<Feature>/Services` | Orquesta la lógica de negocio y las reglas de `domain-model.md` (ej. "un Provider solo edita su propia Company"). Es la única capa que puede empezar/confirmar una transacción de EF Core. |
| `Modules/<Feature>/Repositories` | Encapsula el acceso a datos de **su** agregado (consultas EF Core). No conoce reglas de negocio, solo sabe construir queries. |
| `Modules/<Feature>/Dtos` | Contratos de entrada/salida de la API. Nunca se exponen las `Entities` de EF directamente. |
| `Modules/<Feature>/Entities` | Clases POCO mapeadas a tablas (`docs/database-design.md`). Viven en el módulo, no en `Infrastructure`, para que el módulo sea autocontenido. |
| `Modules/<Feature>/Persistence` | `IEntityTypeConfiguration<T>` de EF Core para las entidades del módulo (mapeo a snake_case, FKs, índices, `CHECK`). El `DbContext` las descubre con `ApplyConfigurationsFromAssembly`, así nunca crece un archivo gigante de configuración. |
| `Infrastructure/Database` | El `DbContext` en sí (registro de `DbSet`s, convención snake_case, enums de Postgres), y **todas** las migraciones (EF Core exige una sola carpeta de migraciones por contexto, aunque las entidades vivan repartidas en los módulos). |
| `Infrastructure/Security` | Implementación técnica de JWT, hashing de contraseñas, y el mecanismo de "quién soy y de qué empresa soy" (`CurrentUserContext`) que usan los módulos para autorizar. |
| `Infrastructure/Logging` | Configuración de Serilog (sinks, enrichers), sin lógica de negocio. |
| `Shared/Exceptions` | Excepciones de dominio comunes a todos los módulos, mapeadas centralmente a códigos HTTP por el middleware global. |
| `Shared/Responses` | Formas de respuesta reutilizadas por más de un módulo (ej. `PagedResult<T>` para listados con filtros — UC-T-04/06). No es un "envelope" genérico obligatorio: una respuesta exitosa simple devuelve su DTO directamente. |
| `Shared/Validation` | Reglas de validación transversales (ej. moneda ISO 4217) que se usarían en DTOs de varios módulos (`Experience.Currency`, `Package.Currency`, etc.). |

**Por qué no hay una capa `Domain`/`Application`/`Infrastructure` al estilo Clean Architecture completa:** para el tamaño actual del proyecto agregaría carpetas e interfaces que no cambian ninguna decisión (no hay múltiples implementaciones intercambiables de persistencia, ni necesidad de aislar el dominio de EF Core). El límite real que importa es **por caso de uso/módulo**, no por capa técnica extra.

---

## 1–2. Flujo Controller → Service → Repository

Ejemplo concreto (UC-AUTH-02, Login):

```
POST /api/auth/login  (LoginRequest)
   │
   ▼
AuthController.Login(LoginRequest dto)
   │  - [ApiController] ya valida DataAnnotations antes de entrar aquí
   ▼
IAuthService.LoginAsync(dto)                     // Modules/Auth/Services
   │  1. IUserRepository.GetByEmailAsync(email)
   │  2. IPasswordHasherService.Verify(user.PasswordHash, password)
   │  3. si falla -> throw UnauthorizedAppException
   │  4. ITokenService.GenerateAccessToken(user)  // firma JWT con claims
   │  5. ITokenService.GenerateRefreshToken()
   │  6. IRefreshTokenRepository.AddAsync(...)
   │  7. SaveChangesAsync (Unit of Work = el propio DbContext scoped)
   ▼
retorna AuthTokensResponse (access_token, refresh_token, expires_in)
   ▼
Controller devuelve 200 OK con el DTO
```

Regla fija: **un Controller nunca llama a un Repository directamente**, y **un Repository nunca contiene reglas de negocio** (ni siquiera un `if`) — solo queries. Si un Service necesita una consulta nueva, se agrega un método al Repository, no se hace `DbContext` inline en el Service (así se puede testear el Service con un repositorio fake sin levantar Postgres).

---

## 3. Inyección de dependencias

Registro por módulo en `Program.cs`, usando métodos de extensión `AddAuthModule()`, `AddCompaniesModule()`, etc. (uno por `Modules/<Feature>`, definido en ese mismo folder) para que agregar un módulo nuevo no obligue a tocar un archivo central gigante:

```csharp
// Modules/Auth/AuthModuleExtensions.cs
public static IServiceCollection AddAuthModule(this IServiceCollection services)
{
    services.AddScoped<IUserRepository, UserRepository>();
    services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
    services.AddScoped<IAuthService, AuthService>();
    services.AddScoped<ITokenService, JwtTokenService>();
    return services;
}
```

**Lifetimes:**
- `TurisClickDbContext`: **Scoped** (una instancia por request — es el Unit of Work implícito).
- Repositories y Services: **Scoped** (dependen del `DbContext`).
- `IPasswordHasherService`: **Singleton** (sin estado, `PasswordHasher<T>` es thread-safe).
- `ICurrentUserContext`: **Scoped**, se resuelve leyendo `IHttpContextAccessor.HttpContext.User` (claims del JWT ya validado).
- `IHttpContextAccessor`: **Singleton** (registrado por ASP.NET Core, `AddHttpContextAccessor()`).

---

## 4. Manejo global de errores

Un único `IExceptionHandler` (patrón nativo de ASP.NET Core, registrado con `app.UseExceptionHandler()`), sin `try/catch` repetidos en cada Controller. Mapea excepciones de `Shared/Exceptions` a `ProblemDetails` (RFC 7807):

| Excepción | HTTP Status |
|---|---|
| `NotFoundAppException` | 404 |
| `ConflictAppException` (ej. sin cupo, email duplicado) | 409 |
| `ForbiddenAppException` (ej. falla UC-SYS-03) | 403 |
| `UnauthorizedAppException` (credenciales inválidas) | 401 |
| `ValidationAppException` (regla de negocio inválida, no de `ModelState`) | 400 |
| Cualquier otra excepción no controlada | 500, logueada con Serilog como `Error`, sin exponer detalles internos en producción |

Los errores de validación de `ModelState` (DataAnnotations) los devuelve automáticamente `[ApiController]` como 400 + `ValidationProblemDetails`, sin pasar por el handler.

---

## 5. Autenticación JWT

- `AuthService` genera un **access token** JWT corto (ej. 15 min) firmado con `Jwt:Key` (HMAC-SHA256), con claims: `sub` (UserId), `email`, `role`, y `company_id` **solo si** `Role = PROVIDER`.
- El **refresh token** es un valor aleatorio opaco (no JWT), guardado hasheado en `refresh_tokens.token_hash`, con `expires_at` largo (ej. 30 días). UC-AUTH-03 lo intercambia por un access token nuevo; UC-AUTH-04 lo marca `revoked_at`.
- `PasswordHasherService` envuelve `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (PBKDF2 con salt, HMAC-SHA256, iteraciones por defecto de .NET) — no se implementa hashing propio.
- Middleware: `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` validando issuer, audience, firma y expiración; `AddAuthorization()` con las políticas de la sección siguiente.

---

## 5.1. CORS (frontend Backoffice)

El Backoffice (web, React + Vite) corre en un origen distinto (`http://localhost:5173` en dev) y llama a la API desde el navegador — sin CORS habilitado, el browser bloquea esas respuestas aunque el request en sí funcione (a diferencia de Postman/Newman, que no aplican CORS).

- Los orígenes permitidos se leen de configuración (`Cors:AllowedOrigins`, un array), nunca hardcodeados en `Program.cs` — `appsettings.Development.json` ya trae `http://localhost:5173`. Para agregar otro origen (otro puerto, otro ambiente) se edita configuración, no código.
- La policy `"Frontend"` habilita cualquier header/método pero **no** `AllowCredentials()`: la sesión viaja por header `Authorization: Bearer ...`, no por cookies, así que no hace falta CORS con credenciales (evita la combinación `AllowAnyOrigin` + `AllowCredentials`, que ni siquiera es válida en ASP.NET Core).
- **Tourist Mobile (React Native/Expo) no necesita esta policy**: CORS es un mecanismo del navegador, y una app nativa no está sujeta a esa restricción — sus requests a la API no pasan por el chequeo de CORS del backend.

---

## 6. Autorización por ADMIN / PROVIDER / TOURIST

Autorización basada en el claim `role` del JWT, con policies explícitas (más legible que `[Authorize(Roles = "...")]` repetido):

```csharp
options.AddPolicy("RequireAdmin", p => p.RequireRole("ADMIN"));
options.AddPolicy("RequireProvider", p => p.RequireRole("PROVIDER"));
options.AddPolicy("RequireTourist", p => p.RequireRole("TOURIST"));
```

Cada Controller aplica `[Authorize(Policy = "RequireProvider")]` a nivel de acción o de clase, según lo defina `use-cases.md` (ej. todo `CompaniesController.UpdateMyCompany` es `RequireProvider`; `AdminController` entero es `RequireAdmin`). Los endpoints de exploración/búsqueda (UC-T-03..07) quedan `[AllowAnonymous]`, tal como indica su documentación de caso de uso.

---

## 7. UC-SYS-03 — Aislamiento por `company_id`

Se implementa en **dos niveles**, no como un `CHECK` de base de datos (`database-design.md` ya documenta por qué eso no es posible ahí):

1. **`ICurrentUserContext`** (`Infrastructure/Security/CurrentUserContext.cs`): lee del JWT del request actual `UserId`, `Role` y `CompanyId?` (nulo si no es PROVIDER).
2. **`CompanyOwnershipGuard`** (`Infrastructure/Security/CompanyOwnershipGuard.cs`): método `void EnsureOwns(Guid resourceCompanyId)` que compara `resourceCompanyId` contra `ICurrentUserContext.CompanyId` y lanza `ForbiddenAppException` si no coincide.

Uso típico dentro de un Service (ej. `ExperienceService.UpdateAsync`):

```csharp
var experience = await _repository.GetByIdAsync(id)
    ?? throw new NotFoundAppException("Experience no encontrada");

_ownershipGuard.EnsureOwns(experience.CompanyId);   // UC-SYS-03

// ... aplicar cambios
```

No se modela como un `[Authorize]` con requirement genérico porque el `companyId` a validar casi siempre depende de **la entidad ya cargada** (no viene en la URL) — por eso vive en el Service, después de leer el recurso, no antes en un filtro de acción.

---

## 8. DTOs

- Un DTO de **request** y uno de **response** por operación (no se reutiliza el mismo DTO para leer y escribir, aunque se parezcan, porque evolucionan por separado).
- Viven en `Modules/<Feature>/Dtos/`, nunca se expone una `Entity` de EF directamente en un Controller.
- Mapeo **manual** (métodos de extensión `ToResponse()` / `ToEntity()` en el propio módulo) en vez de AutoMapper: con el volumen actual de entidades no se justifica una dependencia y una capa de configuración de mapeo adicionales; se reconsidera solo si el mapeo se vuelve realmente repetitivo.

---

## 9. Validaciones

Dos niveles, sin agregar FluentValidation todavía (no está en el listado de paquetes iniciales; se reevalúa si las reglas cruzadas entre campos se vuelven difíciles de expresar con DataAnnotations):

1. **Formato/forma** (`DataAnnotations` en los DTOs: `[Required]`, `[EmailAddress]`, `[MinLength]`, y atributos propios en `Shared/Validation` como `[Iso4217Currency]`) — los resuelve automáticamente `[ApiController]` antes de llegar al Service.
2. **Reglas de negocio** (ej. "la empresa debe estar `APPROVED`", "el email no debe existir ya") — se validan **dentro del Service**, porque necesitan consultar el repositorio o comparar contra otro recurso; lanzan `ConflictAppException`/`ValidationAppException`, capturadas por el handler global (punto 4).

---

## 10. Logging

Serilog configurado en `Infrastructure/Logging/SerilogConfigurator.cs`, inicializado en `Program.cs` antes de construir el host (para capturar errores de arranque también):

- Sinks de desarrollo: consola + archivo rotado diario (`logs/turisclick-.log`, en `.gitignore`).
- Enrichers: `CorrelationId` (por request, vía middleware), `UserId`/`Role` cuando el request está autenticado.
- Los logs de negocio relevantes (login fallido, reserva creada, cupo agotado) se loguean explícitamente desde el Service con nivel apropiado (`Information`/`Warning`); no se loguea cada request genérico dos veces (se usa el logging de requests incorporado de Serilog, `UseSerilogRequestLogging()`).
- Preparado para agregar el sink de Azure Application Insights más adelante sin tocar el resto del código (solo se agrega el sink en `SerilogConfigurator`).

---

## 11. Entity Framework Core

- **Un solo `DbContext`** (`TurisClickDbContext`) para todo el proyecto — separar por módulo en varios `DbContext` complicaría las transacciones cruzadas (ej. UC-SYS-05 crea `Reservation` + `ReservationItem` + actualiza `ExperienceAvailability` en una sola operación) sin ningún beneficio real a este tamaño.
- Convención de nombres: se configura Npgsql para mapear a `snake_case` automáticamente (tablas y columnas), consistente con `database-design.md`.
- Los enums de C# se mapean a los `ENUM` nativos de Postgres ya definidos en FASE 3 (`user_role`, `reservation_status`, etc.) usando `HasPostgresEnum` + `HasConversion<string>` por columna.
- Cada módulo aporta sus `IEntityTypeConfiguration<T>`; el `DbContext` las carga con `modelBuilder.ApplyConfigurationsFromAssembly(typeof(TurisClickDbContext).Assembly)` — así el `DbContext` no necesita conocer cada entidad nueva a mano.
- Connection string **nunca** en `appsettings.json`: se lee de `dotnet user-secrets` en desarrollo y de variables de entorno / Azure Key Vault en producción (ver sección de configuración más abajo).

---

## 12. Migraciones

- Todas viven en `Infrastructure/Database/Migrations/` (una sola carpeta, aunque las entidades estén repartidas en módulos — es una limitación/convención normal de EF Core).
- **Una migración por oleada** de `use-cases.md`, no una migración gigante con las 20 tablas de una vez. Ejemplo: `0001_Oleada0_Auth`, `0002_Oleada1_CatalogoYProveedores`, etc.
- Comando estándar (ejecutado desde `src/TurisClick.Api`):
  ```bash
  dotnet ef migrations add 0001_Oleada0_Auth --output-dir Infrastructure/Database/Migrations
  dotnet ef database update
  ```
- Los tipos `ENUM` de Postgres que no correspondan todavía a ninguna tabla de la oleada actual **no se crean antes de tiempo** — cada migración solo agrega lo que su oleada necesita, igual que las tablas.

---

## 12.1. Seed de datos de DEVELOPMENT

`Infrastructure/Database/Seed/DevelopmentSeeder.cs` reemplaza los inserts manuales ad hoc: admin de prueba, categorías base y la jerarquía real de destinos de Bolivia (País → Departamento → Ciudad, desde `bolivia-cities.json`).

- **Se ejecuta solo si `IsDevelopment() && Seed:Enabled=true`** (doble gate a propósito, AND no OR) — `appsettings.Development.json` ya trae `Seed:Enabled: true`; nunca corre en Production porque ese flag no existe en `appsettings.json`.
- **Idempotente**: cada paso comprueba existencia antes de insertar (por email, por nombre de categoría, por nombre+tipo+padre de destino) — correr el seed en cada arranque de `dotnet run` en dev no duplica nada.
- **La contraseña del admin nunca está en código**: se lee de `Seed:AdminPassword` vía `dotnet user-secrets` — si falta, el seed omite *solo* ese paso (loguea un warning) y sigue con categorías/destinos. Configurarla una vez por máquina:
  ```bash
  dotnet user-secrets set "Seed:AdminPassword" "AdminPassword123!"
  ```
- El hash se genera con el mismo `IPasswordHasherService` que usa `AuthService` — no hay una segunda implementación de hashing para seeds.
- **Limpieza de destinos dummy de test/Postman**: además de sembrar datos, el seed también *limpia* — en cada arranque, `CleanupDummyTestDestinationsAsync` busca destinos cuyo nombre empiece con `"Ciudad-"`, `"País-"` o `"Región-"` (el patrón que usan las corridas de Postman/Newman para sus jerarquías descartables) y los borra. Antes de borrar un destino tipo CITY que todavía tiene una `Experience`/`Package` real apuntándole (con reservas/pagos reales encima, típico de pruebas manuales), reasigna esos productos a "La Paz" en vez de dejar que la FK bloquee el borrado o perder esa data. Es un no-op silencioso si no encuentra ningún destino con ese patrón — no hace falta correrlo a mano ni repetirlo.

---

## 13. Transacciones para reservas y control de cupos

Este es el punto más sensible a condiciones de carrera (UC-SYS-06). Enfoque: **transacción de EF Core + UPDATE condicional atómico**, sin necesidad de locks explícitos (`SELECT ... FOR UPDATE`):

```csharp
await using var tx = await _db.Database.BeginTransactionAsync();

var affectedRows = await _db.ExperienceAvailabilities
    .Where(a => a.Id == availabilityId && a.ReservedSlots + travelers <= a.TotalSlots)
    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedSlots, a => a.ReservedSlots + travelers));

if (affectedRows == 0)
    throw new ConflictAppException("No hay cupo suficiente para la fecha seleccionada.");

// crear Reservation + ReservationItem dentro de la misma transacción
await _db.SaveChangesAsync();
await tx.CommitAsync();
```

- El `WHERE reserved_slots + @travelers <= total_slots` dentro del mismo `UPDATE` es lo que hace la operación atómica: si dos reservas concurrentes compiten por el último cupo, Postgres serializa las dos escrituras a nivel de fila y solo una cumple la condición — la otra recibe `affectedRows == 0` sin necesidad de un lock manual.
- UC-SYS-05 (reserva múltiple desde itinerario IA) repite este mismo `UPDATE` condicional por cada `AiItineraryItem` **dentro de una única transacción**: si cualquiera de los productos ya no tiene cupo, se hace `RollbackAsync()` completo y no se crea ninguna reserva parcial.
- UC-SYS-08 (liberar cupo) hace la operación inversa (`ReservedSlots - travelers`), también dentro de transacción, al cancelar o expirar.

---

## 14. Preparación para RAG (sin implementarlo todavía)

- `Modules/Ai/` se crea vacío (o con un `README.md` de intención) desde ya en la estructura, pero **no** recibe entidades/Services reales hasta la Oleada 5+ — evita que el módulo quede a medio implementar.
- Se reservan nombres de interfaz (`IRetrievalService`, `IItineraryComposer`) en la documentación, no en código todavía, para que cuando se implemente RAG el resto del backend (Experiences/Packages/Reservations) no tenga que cambiar: la IA solo va a **leer** datos reales a través de los Repositories/Services ya existentes de esos módulos (principio "la IA no inventa productos" de `domain-model.md`).
- Postgres ya soporta la extensión `pgvector` para almacenar embeddings si se decide un RAG con retrieval vectorial dentro de la misma base de datos (evita sumar una base de datos vectorial separada) — **no se instala todavía**, es solo la opción por defecto a evaluar cuando llegue esa oleada.
- No se agrega ningún paquete NuGet de IA en el bootstrap (Azure OpenAI SDK, Semantic Kernel, etc.) hasta que el caso de uso correspondiente lo requiera explícitamente.

### Estado real tras Oleadas 5–6 (esta sección arriba es el plan previo)

- `Modules/Ai/` ya está implementado con entidades, Repositories, Services, DTOs y Controllers propios, siguiendo la misma estructura que el resto de los módulos.
- `IRetrievalService` se implementó con el nombre reservado. `IItineraryComposer` **no** existe como interfaz separada: la composición vive en `IAiModelClient.ComposeItineraryAsync`, porque es la única parte del proceso que delega en el LLM — separar una interfaz más solo agregaba indirección. El resto del pipeline (filtrado, ranking, package-first, validación anti-hallucination, versionado) es código determinístico en `AiConversationService`/`RetrievalService`.
- Interfaces del módulo: `IAiModelClient` (con dos implementaciones seleccionadas por `Ai:Provider` — `DeterministicAiModelClient`, sin dependencias externas y usado por tests/Newman, y `OllamaAiModelClient`, HTTP contra un Ollama local), `IRetrievalService`, `IItineraryRevalidationService` (Oleada 6, contrasta el snapshot persistido contra el catálogo vigente sin escribir), `IAiConversationService` y `IAiItineraryService`.
- **No se agregó base de datos vectorial ni `pgvector`**: el retrieval es estructurado en Postgres (destino, fechas, capacidad, categorías, precio) y hasta ahora cubre los casos de uso sin necesidad de embeddings. La opción sigue abierta si aparece una necesidad real de match semántico.
- Sí se agregaron paquetes de IA: ninguno. `OllamaAiModelClient` habla HTTP/JSON con `HttpClient` y `System.Text.Json`, sin SDK propietario.
- **UC-SYS-09 (reindexado) no requiere implementación** con este diseño: el retrieval consulta Postgres directamente, así que no hay índice secundario que sincronizar y cualquier cambio del proveedor es visible en la consulta siguiente apenas commitea. Ver la ficha del UC en `use-cases.md` para el detalle y los tests que lo demuestran.

---

## 15. Booking desde un itinerario IA (Oleada 7)

Convertir una propuesta de IA en una reserva real (UC-T-18) cruza dos módulos, y la regla es que **la IA orquesta pero no reserva**:

- `AiItineraryBookingService` (módulo Ai) valida pertenencia y estado, revalida cada componente contra Postgres (UC-SYS-01/02) y arma las líneas con datos **derivados en el servidor** — producto, availability, `CompanyId`, precio y moneda salen de la base, nunca del request, que solo lleva el id del itinerario y `acceptPriceChanges`.
- `ReservationBookingService` (módulo Reservations) es el **único** lugar que toca cupos: toma los holds con el mismo `UPDATE` condicional de UC-SYS-06 y arma `Reservation` + `ReservationItem`s. No abre ni commitea la transacción.
- **Frontera transaccional:** la abre el servicio de IA porque la unidad atómica incluye también la transición `AiItinerary → BOOKED`. Dentro entran los holds, la reserva, sus ítems, el vínculo y el cambio de estado; cualquier fallo revierte todo y no queda ni un hold huérfano.

Tres detalles de concurrencia que conviene no perder de vista:

1. **Los holds se agrupan por availability antes de ejecutarse.** Dos ítems del itinerario pueden apuntar al mismo slot; con un `UPDATE` por ítem cada uno evaluaría la capacidad por separado y la suma podría pasarse.
2. **Se adquieren en orden determinístico** (`ProductType`, luego `AvailabilityId`) para que dos bookings concurrentes que compiten por las mismas filas las tomen siempre en el mismo orden y no se traben entre sí. No hacen falta locks distribuidos.
3. **La revalidación previa es UX, no control de concurrencia.** Entre revalidar y tomar el cupo otro request puede consumirlo; la autoridad final es siempre el `UPDATE` condicional y su `affectedRows`.

**Snapshots: dos mundos separados.** `AiItineraryItem.EstimatedUnitPrice` es el histórico de lo que el turista vio y no se reescribe nunca. `ReservationItem.UnitPrice` es el snapshot comercial, congelado con el precio vigente al reservar. Si difieren, el booking se detiene y pide aceptación explícita (misma política que UC-T-19); aceptar cambia el snapshot de la reserva, jamás el de la IA.

**Idempotencia.** `reservations.ai_itinerary_id` tiene un índice único parcial (migración 0007) que materializa el 1—1 que ya documentaba el modelo de dominio. Es lo que hace segura la doble solicitud concurrente: un `if (status != BOOKED)` en C# no alcanza, porque dos requests pueden leer el estado viejo antes de que ninguno escriba.

---

Arquitectura documentada y lista para bootstrap. Continúo con **FASE 5 — bootstrap físico del proyecto**.

---

## 16. Expiración, cancelación y sanciones (Oleada 8)

**La transición de estado es la autoridad, no el reloj ni el chequeo previo.** Expirar, cancelar y confirmar un pago compiten por la misma fila de `reservations`, y las tres lo resuelven igual: un `UPDATE` condicional sobre el estado esperado y un chequeo de `affectedRows`. Solo quien afecta 1 fila ejecuta el efecto (liberar o retener cupo). De ahí salen tres propiedades sin infraestructura extra:

- **Idempotencia:** expirar o cancelar dos veces libera el cupo una sola vez — la segunda ejecución no gana la transición y no toca nada.
- **Multi-instancia:** dos backends procesando el mismo lote no se pisan; no hacen falta locks distribuidos ni `FOR UPDATE SKIP LOCKED` (que además no encajaría con una transacción por reserva).
- **Pago vs expiración:** `PayAsync` llama al gateway fuera de la transacción y luego intenta la transición condicional a `CONFIRMED`. Si la expiración ganó en el medio, ese `UPDATE` afecta 0 filas y el pago responde 410 `RESERVATION_NO_LONGER_PAYABLE` en vez de confirmar una reserva cuyo cupo ya se liberó.

**Frontera transaccional.** Expirar una reserva es una sola transacción: transición + liberación de todos sus holds + ítems a `EXPIRED` + `AiItinerary BOOKED → SAVED` si aplica. Una transacción **por reserva** y no por lote, para que un fallo aislado no impida procesar el resto. Cancelación del turista y cancelación parcial del proveedor siguen el mismo patrón sobre su propio alcance.

**El proceso de fondo es deliberadamente tonto.** `ReservationExpirationBackgroundService` solo despierta cada N segundos y llama a `IReservationExpirationService`; no tiene ninguna regla de negocio. Así los tests ejercitan la lógica invocando el servicio y nunca esperan un timer real (en el entorno de tests el proceso se apaga por configuración).

**Sanciones administrativas: aplicarlas, no solo registrarlas.** Cambiar un enum en la base no es una sanción. `SUSPENDED` en un usuario ya bloqueaba login y refresh; en contenido, además de sacarlo del catálogo, ahora impide que el proveedor lo republique (antes podía anular la sanción llamando a `publish`); y en una empresa actúa como **filtro de visibilidad y de operación** —catálogo público, retrieval de la IA, creación de reservas y publicación— **sin cascada** sobre el estado de sus productos, para que reactivarla no tenga que "restaurar" nada.
