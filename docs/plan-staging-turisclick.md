# Entorno de staging para TurisClick — análisis de viabilidad

**Documento de investigación. Nada ejecutado.** No se creó ningún recurso en Azure, no se
tocó V1 ni V2, no se modificó ninguna base, ni migraciones, ni código, ni pipelines, ni
Jira. Ningún secreto reproducido: abajo sólo aparecen **nombres** de variables y de
secretos, nunca sus valores.

---

# 1. AUDITORÍA DE LA INFRAESTRUCTURA ACTUAL

## 1.1 Lo que hay

| Recurso | Nombre | Quién lo gestiona |
|---|---|---|
| Resource group | `rg-turisclick-dev` | **Compartido con V1.** Terraform lo lee como *data source*, no lo crea |
| PostgreSQL Flexible Server | `turisclick-postgres-jpm` | **Existente y compartido.** Terraform lo lee como *data source*: **nunca lo gestiona** |
| Base de datos V1 | `turisclick_db` | Fuera de Terraform |
| Base de datos V2 | `turisclick_db_v2` | Terraform, con `prevent_destroy` |
| App Service Plan | `asp-turisclick-v2-dev` | Terraform · SKU **`F1`** |
| Linux Web App | `app-turisclick-v2-api` | Terraform · .NET 10 |
| Key Vault | `kv-turisclick-v2-dev` | Terraform · SKU `standard` |
| Storage (imágenes) | `stturisclickv2imgjpm` | Terraform · Standard / Hot |
| Static Web App (Backoffice) | `ashy-rock-0dd3b480f` | Fuera de Terraform |
| State de Terraform | `stturisclicktfjpm` / `turisclick-v2-dev.tfstate` | Bootstrap, con acceso sólo por Entra ID |

Región: **brazilsouth**.

## 1.2 Configuración y secretos — cómo están resueltos

Terraform **sólo conoce los nombres** de los secretos; los valores se cargan aparte con
`infra/bootstrap/set-keyvault-secrets.ps1`. Los tres nombres son
`db-connection-string`, `jwt-key` y `duffel-test-token`.

Los app settings relevantes se inyectan como **referencias a Key Vault**, resueltas por la
identidad administrada de la app:

```
ConnectionStrings__DefaultConnection = @Microsoft.KeyVault(...)
Jwt__Key                             = @Microsoft.KeyVault(...)
```

Y los no sensibles van literales: `ASPNETCORE_ENVIRONMENT`, `Ai__Provider`,
`Reservations__Expiration__Enabled`, `ImageStorage__*`.

> **Ningún secreto entra en la configuración ni en el state de Terraform.** Está bien
> resuelto y no hace falta cambiarlo para staging.

## 1.3 El pipeline

`.github/workflows/backend-v2.yml`. Dos jobs:

- **Build, tests y publish** — en `push` y `pull_request`.
- **Deploy** — con la compuerta que se puso en la Oleada 14:
  ```
  if: github.event_name == 'workflow_dispatch' && inputs.deploy && github.ref == 'refs/heads/master'
  ```
  Autenticación por **OIDC**, sin secretos de larga vida.

**Limitación para staging**: el destino está **fijo en el `env` del job**
(`RESOURCE_GROUP`, `WEB_APP`, `APP_URL`). Desplegar a otra Web App exigiría
**parametrizar el workflow**, que hoy está prohibido.

## 1.4 Proveedor de vuelos y pagos — verificado en código

| | |
|---|---|
| `var.flights_provider` | **`"Fake"`** por defecto; sólo admite `Fake` o `Duffel` |
| Azure V2 hoy | **Fake** — la búsqueda devuelve `inventoryMode: "SIMULATED"`, `testMode: true` |
| Duffel | **Apagado.** Con `Duffel`, el token es una referencia a Key Vault y `DuffelFlightProvider` **rechaza cualquier token que no empiece con `duffel_test_`** |
| Pasarela de pago | **`SimulatedPaymentGateway` es la única implementación de `IPaymentGateway`** en todo el repositorio, registrada incondicionalmente |

`ChargeAsync` decide el resultado con un booleano que manda el cliente. Sin SDK, sin
tarjeta, sin red. `VoidAsync` sólo escribe en el log.

## 1.5 Qué se puede reutilizar y qué necesita instancia propia

| Recurso | Staging |
|---|---|
| **PostgreSQL Flexible Server** | ✅ **Reutilizable** — basta una base nueva en el mismo servidor |
| **Resource group** | ✅ Reutilizable |
| **Código de la API** | ✅ Sin cambios |
| **Terraform** | ✅ **Ya parametrizado por `var.environment`** |
| **Expo y Backoffice** | ✅ **Sin cambios de código**: ya aceptan `EXPO_PUBLIC_API_BASE_URL` y `VITE_API_BASE_URL` |
| **Base de datos** | ❌ Instancia propia: `turisclick_db_staging` |
| **App Service Plan + Web App** | ❌ Instancia propia |
| **Key Vault** | ❌ Instancia propia — **imprescindible para aislar `jwt-key`** |
| **Storage de imágenes** | ❌ Propio, o un contenedor aparte |
| **State de Terraform** | ❌ Clave propia: `turisclick-v2-staging.tfstate` |
| **Workflow de deploy** | ❌ Parametrizar, o desplegar a mano |

---

# 2. VIABILIDAD

> ## ✅ Sí, es técnicamente viable.

Y es **más fácil de lo esperable**, por tres razones que ya están resueltas en el proyecto:

1. **Terraform está parametrizado**: `environment`, `database_name`, `web_app_name`,
   `key_vault_name`, `flights_provider` son todas variables. Un `staging.tfvars` nuevo
   basta, sin tocar un solo `.tf`.
2. **Los dos frontends ya aceptan una URL arbitraria** por variable de entorno. Cero
   cambios de código.
3. **El proveedor falso y el pago simulado ya son el comportamiento por defecto.** No hay
   que desactivar nada para evitar cargos: hay que *no activar* Duffel.

## 2.1 Cumplimiento de las condiciones pedidas

| Condición | ¿Se cumple? | Cómo |
|---|---|---|
| API independiente | ✅ | Web App propia |
| PostgreSQL independiente | ✅ | Base propia en el servidor existente |
| Datos ficticios | ✅ | `tools/demo-catalog` ya genera el catálogo |
| Usuarios de prueba independientes | ✅ | Base propia ⇒ usuarios propios |
| Backoffice de pruebas | ✅ | El mismo, con `VITE_API_BASE_URL` apuntando a staging |
| Expo apuntando a staging | ✅ | `EXPO_PUBLIC_API_BASE_URL` |
| Proveedor de vuelos simulado | ✅ | `flights_provider = "Fake"` (default) |
| Pagos simulados | ✅ | Única pasarela existente |
| Sin emisión ni cargos reales | ✅ | Ver 1.4 |
| Acceso restringido | 🟡 **Parcial** | Ver abajo |

**Sobre el acceso restringido**: una Web App en SKU **F1 no admite restricciones de red
privada**. Las opciones reales son *restricciones de IP* (`ip_restriction`, disponible en
F1) o dejarla pública con una URL que nadie conoce. **No es aislamiento fuerte.** Para
aislamiento de red haría falta subir de SKU, lo que cuesta.

---

# 3. LAS TRES ALTERNATIVAS

> **Sobre los precios**: no invento cifras. Lo que es estructuralmente verificable en el
> repositorio y en la documentación de Azure lo afirmo; **los importes mensuales concretos
> para brazilsouth hay que cotizarlos** en la calculadora de Azure. Lo marco explícitamente.

## A · Staging permanente en Azure

| | |
|---|---|
| **Recursos** | Plan F1 + Web App + Key Vault + base nueva + storage + state propio |
| **Costo** | App Service **F1 es el nivel gratuito** ⇒ 0 por el plan. La base **no agrega servidor**: reutiliza el existente, sólo suma almacenamiento. **Key Vault y Storage tienen costo por operación/GB: a cotizar.** Estimación estructural: **cercano a cero**, pero **sin cifra verificada** |
| **Complejidad** | Media |
| **Tiempo** | 3–5 h la primera vez |
| **Ventajas** | Fidelidad total con producción; queda disponible para futuras oleadas y para la defensa |
| **Desventajas** | Superficie permanente que mantener y vigilar; F1 tiene cuota de CPU diaria y no tiene Always On |
| **Riesgo para producción** | 🟡 **Medio.** El resource group y el servidor PostgreSQL son **compartidos**. Un `apply` mal apuntado puede tocar recursos de V2 |
| **¿Cubre las 19 pruebas?** | ✅ Las 19 |

## B · Staging temporal en Azure, sólo durante las pruebas

| | |
|---|---|
| **Recursos** | Los mismos, creados y destruidos |
| **Costo** | El de A, prorrateado a horas ⇒ **marginal**. **A cotizar igual** |
| **Complejidad** | **Alta.** `azurerm_postgresql_flexible_server_database.v2` tiene **`prevent_destroy = true`**: destruir exige intervención deliberada, y ahí está el riesgo |
| **Tiempo** | 3–5 h la primera vez + 1 h cada ciclo |
| **Ventajas** | Fidelidad total, sin superficie permanente |
| **Desventajas** | El ciclo crear/destruir sobre un RG y un servidor compartidos es donde más fácil se comete un error caro |
| **Riesgo para producción** | 🔴 **Alto.** Un `destroy` con el tfvars equivocado apunta a los recursos de producción |
| **¿Cubre las 19 pruebas?** | ✅ Las 19 |

## C · Staging local con PostgreSQL independiente

| | |
|---|---|
| **Recursos** | La API local (`dotnet run`), una base local nueva, Expo en el teléfono por LAN |
| **Costo** | **0.** Sin cifras que cotizar |
| **Complejidad** | **Baja.** Ya existe: `EXPO_PUBLIC_API_TARGET=local` está documentado en `.env.example`, con `EXPO_PUBLIC_API_BASE_URL=http://192.168.x.x:5288` para el teléfono |
| **Tiempo** | **30–60 min** |
| **Ventajas** | **Riesgo nulo para producción**; iteración inmediata; se puede repetir cuantas veces haga falta; la base se borra y se recrea en segundos |
| **Desventajas** | **No valida el despliegue**, valida el código. Expo necesita misma LAN |
| **Riesgo para producción** | 🟢 **Nulo** — ninguna petición sale hacia Azure |
| **¿Cubre las 19 pruebas?** | ✅ Las 19, **funcionalmente** |

---

# 4. SEGURIDAD Y AISLAMIENTO — VERIFICACIONES CONCRETAS

## 4.1 Que ninguna prueba toque la base de producción

**Mecanismo**: cadena de conexión distinta. En local, `ConnectionStrings__DefaultConnection`
apunta a una base nueva; la de producción vive en Key Vault y la app local no tiene la
identidad administrada para leerla.

**Verificación propuesta**
1. Antes: `GET /api/packages` contra **Azure V2** → registrar `totalCount` (hoy **13**).
2. Ejecutar toda la batería contra staging.
3. Después: repetir contra Azure V2 → **`totalCount` tiene que seguir siendo 13**.
4. Contar reservas por la API de administración de Azure antes y después → **idéntico**.

## 4.2 Que un usuario de staging no pueda autenticarse en producción

⚠️ **Hallazgo importante.** El `Issuer` y la `Audience` son **los mismos en todos los
ambientes**: `TurisClick.Api` y `TurisClick.Client`, literales en `appsettings.json`.

> **Issuer y audience NO aíslan nada.** El único aislamiento real es **la clave de firma**
> (`Jwt__Key`), porque `Program.cs` valida con `ValidateIssuerSigningKey = true`.

**Condición obligatoria**: staging **tiene que usar una `jwt-key` distinta**. Si se
reutilizara la de producción, **un token de staging sería válido en producción**.

**Verificación propuesta**
1. Autenticarse en staging y obtener un token.
2. Llamar con ese token a un endpoint autenticado de **Azure V2**.
3. **Resultado esperado: 401.** Si devuelve 200, el aislamiento está roto y hay que parar.

## 4.3 Que los pagos simulados no llamen a nada externo

**Verificación propuesta**
1. Revisión de código: una sola implementación de `IPaymentGateway` *(ya hecha)*.
2. Durante el pago de prueba, capturar el tráfico saliente del proceso de la API
   (`netstat`, o un proxy local). **Esperado: ninguna conexión a dominios de pago.**
3. Confirmar en el log la línea *"No se movió dinero porque el cobro era simulado"* al
   cancelar.

## 4.4 Que la suspensión de empresa bloquee TODAS las rutas de reserva

**Ya verificado en código, y no depende sólo del estado de publicación.** La comprobación
está dentro del propio servicio de reservas:

```csharp
// ReservationService.cs:301-305
// UC-A-08: si la empresa está suspendida su catálogo no existe para el turista — tampoco por
// la puerta de atrás de reservar directo una availability cuyo id ya conocía.
if (experience.Status != PublicationStatus.PUBLISHED
    || experience.Company?.Status == CompanyStatus.SUSPENDED)
    throw new NotFoundAppException("Experiencia no encontrada.");
```

Lo mismo para paquetes en la línea 363. Es un **`||` con el estado de publicación**, no un
sustituto. Y además se filtra en `AiCatalogRepository` (retrieval de la IA),
`ItineraryRevalidationService` (itinerarios guardados) y la búsqueda de vuelos.

**Verificación propuesta**
1. Reservar el paquete de prueba → éxito.
2. **Suspender la empresa.**
3. Reintentar la reserva **con el mismo `availabilityId` ya conocido** → **404 esperado**.
4. Buscar el paquete en el catálogo público → **no aparece**.
5. Buscar vuelos en su ruta → **no aparece entre los paquetes**.
6. Reactivar → todo vuelve, sin pérdida de datos.

El paso 3 es el que importa: demuestra que **no alcanza con conocer el id**.

## 4.5 Que un fallo a mitad de prueba no deje datos inconsistentes

**Mecanismo ya existente**: la creación de reservas ocurre en **una sola transacción** —
retención de cupo, reserva, líneas y vínculo— y *cualquier fallo revierte absolutamente
todo*. El cupo se toma con un `UPDATE` condicional atómico, y la expiración por tiempo
libera lo que quede colgado.

**Verificación propuesta**: interrumpir deliberadamente una reserva (cortar la API entre la
retención y el pago) y comprobar que `reservedSlots` vuelve a su valor previo, por la API
de disponibilidad.

## 4.6 Limpieza sin perder evidencia

| Qué | Cómo |
|---|---|
| **Evidencia** | Volcar cada respuesta JSON a archivos, con fecha y código HTTP, **antes** de limpiar |
| **Opción C** | `DROP DATABASE turisclick_db_staging` y recrearla. Segundos, sin residuos |
| **Opciones A/B** | Suspender la empresa de prueba y despublicar el paquete; la base queda, identificable |
| **Nunca** | Borrar reservas: son la evidencia de que el flujo funcionó |

---

# 5. PLAN DE IMPLEMENTACIÓN — ALTERNATIVA RECOMENDADA (C)

## 5.1 Arquitectura

```
Teléfono (Expo Go)                PC de desarrollo
  EXPO_PUBLIC_API_BASE_URL   →    API local  :5288
      http://192.168.x.x:5288       │
                                    ├── PostgreSQL local · turisclick_db_staging
Navegador (Backoffice)              ├── Flights:Provider = Fake
  VITE_API_BASE_URL          →      ├── IPaymentGateway = Simulated
      http://localhost:5288         └── Jwt__Key propia (distinta de producción)

Azure V2  ──  INTACTO, sólo lectura para los contrastes de 4.1
```

## 5.2 Cambios necesarios

| Elemento | Cambio |
|---|---|
| Código de la API | **Ninguno** |
| Terraform | **Ninguno** |
| Pipelines | **Ninguno** |
| `frontend/apps/tourist-mobile/.env` | `EXPO_PUBLIC_API_BASE_URL` a la IP de la PC |
| `frontend/apps/backoffice/.env` | Ya está en `VITE_API_TARGET=local` |
| Configuración local de la API | Base `turisclick_db_staging` y `Jwt__Key` propia, **en user-secrets** |

> **Los dos `.env` son archivos de configuración local.** Modificarlos entra en la
> restricción "no modificar configuraciones", así que **no los toqué** y pido autorización.

## 5.3 Procedimiento

1. Crear la base `turisclick_db_staging` en PostgreSQL local.
2. Configurar user-secrets: cadena de conexión a esa base y una `Jwt__Key` nueva.
3. Aplicar migraciones *(local — `dotnet ef database update`)*.
4. Levantar la API local.
5. Cargar el catálogo ficticio con `tools/demo-catalog`.
6. Crear empresa, operador y turista de prueba por API.
7. Crear el paquete con la regla de vuelo **VVI → LPB**.
8. Ejecutar las **11 pruebas de API**.
9. Apuntar Expo a la IP de la PC y ejecutar las **8 pruebas manuales**.
10. Ejecutar las verificaciones de aislamiento de la sección 4.
11. Volcar la evidencia.
12. `DROP DATABASE` y restaurar los `.env`.

## 5.4 Pruebas de aceptación del entorno

Antes de dar por bueno el staging:

| # | Prueba | Esperado |
|---|---|---|
| S-1 | `GET /api/packages` en staging | Catálogo ficticio, **distinto** del de Azure |
| S-2 | Token de staging contra Azure V2 | **401** |
| S-3 | `totalCount` de paquetes en Azure antes y después | **13 = 13** |
| S-4 | Búsqueda de vuelos en staging | `inventoryMode: "SIMULATED"` |
| S-5 | Tráfico saliente durante el pago | **Ninguna conexión a pasarelas** |

## 5.5 Rollback

No hay rollback que hacer: **no se toca Azure**. La limpieza es borrar la base local y
restaurar los dos `.env`.

## 5.6 Evidencia para el Proyecto de Grado

- Respuestas JSON de los 11 pasos de API, con fecha y código HTTP.
- Capturas de la app en los 8 pasos manuales.
- Las cinco pruebas de aislamiento S-1 a S-5, en particular **S-2 (401) y S-3 (13 = 13)**.
- La demostración de 4.4 paso 3: reservar con un id conocido tras suspender → **404**.

---

# 6. RESPUESTAS

### 1. ¿Es técnicamente viable tener staging independiente?

**Sí**, y con menos trabajo del esperable: Terraform ya está parametrizado por ambiente, los
dos frontends ya aceptan una URL arbitraria, y el proveedor falso y el pago simulado son el
comportamiento por defecto. **Cero cambios de código.**

### 2. ¿Cuál recomiendo y por qué?

**La alternativa C — staging local.**

El objetivo es validar **comportamiento funcional**, y C lo cubre entero con **riesgo nulo
para producción** y media hora de preparación. A y B comparten resource group y servidor
PostgreSQL con producción: un `apply` o un `destroy` mal apuntado es un riesgo real que no
se justifica para una ronda de validación.

**El límite de C, dicho claramente**: valida el **código**, no el **despliegue**. Pero eso
ya está cubierto por otro lado — verifiqué de forma independiente que Azure V2 responde con
el campo `redirect`, que sólo existe desde el commit de la Oleada 19. La combinación
*"el despliegue es el commit correcto"* + *"el commit se comporta bien"* es defendible.

**Si tu tribunal exige validación en la nube**, entonces **A**, no B: el riesgo de B está en
el ciclo de destrucción, que es justamente donde se rompen cosas.

### 3. ¿Cuánto costaría?

- **C: 0.** Sin nada que cotizar.
- **A y B**: el plan F1 **es el nivel gratuito de App Service**, y la base **reutiliza el
  servidor PostgreSQL existente**, así que no hay servidor nuevo. Quedan **Key Vault y
  Storage**, que se cobran por operación y por GB.
  **No pongo una cifra mensual porque no la puedo verificar.** Hay que cotizarla en la
  calculadora de Azure para **brazilsouth**. Estructuralmente, es cercano a cero.

### 4. ¿Qué pruebas de Oleada 19 podríamos completar?

**Las 19 pendientes**, con C:

- **11 por API**: cotizar, revalidar, **reservar paquete + vuelo**, **pagar (la regresión de
  BUG-10)**, confirmar, el paquete en la búsqueda por ruta, presupuesto de cancelación,
  cancelar, libro de pagos.
- **6 con credenciales**: login, permisos, y el **501** de los vuelos de operador.
- **8 manuales en Expo**, con el teléfono apuntando a la API local.

Más las **5 pruebas de aislamiento** S-1 a S-5, que son evidencia adicional para la tesis.

### 5. ¿Qué autorizaciones necesito?

| # | Autorización |
|---|---|
| 1 | **Crear la base local `turisclick_db_staging`** y aplicar migraciones **en local** |
| 2 | **Modificar los dos `.env` locales** de Expo y Backoffice *(reversible; hoy entra en "no modificar configuraciones")* |
| 3 | **Configurar user-secrets locales**: cadena de conexión y `Jwt__Key` propia |
| 4 | **Crear datos de prueba en la base local**: empresa, operador, turista, paquete, reserva y pago simulado |
| 5 | **Ejecutar consultas de sólo lectura contra Azure V2** para los contrastes S-2 y S-3 |
| 6 | **Que ejecutes vos** las 8 pruebas manuales de Expo |

Si preferís A o B, las autorizaciones son otras y mayores: crear recursos en Azure,
un `staging.tfvars` y una clave de state nuevos, y parametrizar el workflow de deploy.

**No ejecuto nada hasta tu aprobación explícita.**
