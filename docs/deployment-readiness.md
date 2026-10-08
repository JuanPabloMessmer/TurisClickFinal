# Estado de preparación para desplegar

Qué está listo, qué no, y qué hay que saber antes de tocar Azure. Escrito el 8 de octubre de 2026, al cerrar
la oleada de preparación para el despliegue.

**Veredicto: NO desplegar todavía.** No porque falte código, sino porque **el backend V2 en Azure está caído
ahora mismo** y arreglarlo requiere aplicar migraciones, que es una decisión que no se toma de paso. El
procedimiento está en [azure-v2-deployment-plan.md](azure-v2-deployment-plan.md).

---

## 1. El incidente: Azure V2 está caído

Verificado contra el backend desplegado el 7 de octubre de 2026:

| Endpoint | Respuesta | Causa |
|---|---|---|
| `GET /api/categories` | 200 | — |
| `GET /api/destinations` | 200 | — |
| `GET /api/experiences` | 200 | — |
| `GET /api/packages` | **500** | `column p.cancellation_policy does not exist` |
| `POST /api/auth/login` | **500** | `column u.must_change_password does not exist` |

**Nadie puede iniciar sesión** y el catálogo de paquetes no carga. Ni el Backoffice desplegado ni la app
móvil apuntada a Azure sirven para nada en este estado.

### Cómo pasó

`.github/workflows/backend-v2.yml` desplegaba el backend en **cada push a master** que tocara `src/` o
`tests/`, y no aplica migraciones (nunca lo hizo: la base V2 se gestiona con los scripts de
`infra/bootstrap`). Los pushes de las Oleadas 12 y 13 subieron código que espera columnas nuevas contra una
base que no las tiene.

El workflow **sí** tenía smoke tests que cubren `/api/packages`, y deben haber fallado. Pero corren *después*
de `az webapp deploy`: cuando avisaron, el código roto ya estaba sirviendo. Un smoke test que corre después
del deploy detecta el problema, no lo evita.

### Qué se cambió para que no vuelva a pasar

1. **El deploy dejó de ser automático.** Ahora sólo corre con `workflow_dispatch` y la casilla `deploy`
   marcada, más un campo para anotar qué migración quedó aplicada. La build y los tests siguen corriendo en
   cada push: esa parte sí servía.
2. **El esquema se verifica antes de empaquetar.** Dos llamadas al backend desplegado tocan las columnas de
   las últimas migraciones. Si la base está atrasada responden 500 con `does not exist` y el workflow se
   detiene **sin haber tocado la app**.

Lo que esto *no* arregla: la aplicación que ya está caída. Eso necesita las migraciones.

---

## 2. Inventario de migraciones

### Lo que se pudo verificar

| Base | Migraciones | Última |
|---|---|---|
| Local `turisclick_v2_dev` | 14 de 14 | `0014_Oleada13_AltaDeOperadoresPorAdmin` |
| Local `turisclick_v2_test` | 14 de 14 | `0014_Oleada13_AltaDeOperadoresPorAdmin` |
| Local `turisclick_v2_demo` | 14 de 14 | `0014_Oleada13_AltaDeOperadoresPorAdmin` |
| Azure `turisclick_db_v2` | **no se pudo leer directamente** | ver abajo |

### Por qué no se pudo leer `__EFMigrationsHistory` de Azure

El firewall del servidor PostgreSQL permite `192.223.106.62`; esta máquina hoy sale por `192.223.106.50`. La
IP rotó. Agregar una regla es modificar un recurso de Azure, y esta oleada es de sólo lectura sobre Azure, así
que no se hizo.

### Lo que sí quedó demostrado, sin acceso a la base

Las migraciones de EF se aplican en orden y el historial es una secuencia: el conjunto aplicado es siempre un
prefijo. Desde afuera, con las respuestas del backend:

- **`0001`–`0009` están aplicadas.** `GET /api/destinations` responde 200 y devuelve `imageUrl`, columna que
  agrega `0009`.
- **`0012`, `0013` y `0014` NO están aplicadas.** `packages.cancellation_policy` (de `0012`) y
  `users.must_change_password` (de `0014`) no existen, confirmado por los mensajes de error de PostgreSQL.
- **`0010` y `0011` quedan indeterminadas.** No hay endpoint anónimo que toque sólo sus columnas: el único
  que lee `packages` falla antes, en `cancellation_policy`, porque EF ordena las columnas alfabéticamente y
  `c` viene antes que `i` de `includes_flight`.

**Esto no bloquea nada.** El script de migración generado es **idempotente**: cada migración va envuelta en
`IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = …)`. Aplica exactamente lo que
falte, sea que la base esté en `0009`, `0010` u `0011`. El primer paso del plan de despliegue es justamente
leer el historial real, que con la IP habilitada toma un comando.

### Qué hacen las migraciones pendientes

Generado con `dotnet ef migrations script 0009 0014 --idempotent` y revisado línea por línea:

| Operación | Cantidad | Detalle |
|---|---|---|
| `CREATE TABLE` | 6 | `flight_quotes`, `flight_bookings`, `package_flight_rules`, `payment_transactions`, `reservation_cancellations`, `reservation_cancellation_lines` |
| `ADD COLUMN` | 25 | 8 son `NOT NULL`, **las 8 con `DEFAULT`** |
| `ALTER COLUMN … TYPE` | 1 | `flight_bookings.status` → `varchar(40)`, en una tabla creada en este mismo lote (vacía) |
| `CREATE INDEX` | 14 | sin `CONCURRENTLY` |
| `DROP INDEX` | 2 | los dos reemplazados inmediatamente por una versión única |
| `ADD CONSTRAINT` (check) | 2 | `ck_packages_cancellation_policy`, `ck_reservation_items_cancellation_policy` |
| **Operaciones destructivas** | **0** | sin `DROP TABLE`, `DROP COLUMN`, `DELETE` ni `TRUNCATE` |
| **Migraciones de datos** | **0** | ningún `UPDATE` ni `INSERT` sobre datos existentes |

**Riesgos reales, nombrados:**

- **Bloqueos.** `CREATE INDEX` sin `CONCURRENTLY` toma `ACCESS EXCLUSIVE` sobre la tabla mientras construye.
  Las tablas indexadas son nuevas o muy chicas, así que son milisegundos. En una base con millones de filas
  habría que usar `CONCURRENTLY`, que EF no genera.
- **No es una sola transacción.** El script trae **5 `START TRANSACTION` / `COMMIT`, uno por migración**. Si
  la tercera falla, las dos primeras quedan aplicadas. No deja un esquema a medias *dentro* de una migración,
  pero sí un prefijo aplicado. Se recupera volviendo a correr el mismo script, que por idempotente retoma
  donde quedó.
- **`ALTER COLUMN … TYPE`** sobre una tabla con datos exigiría reescribirla. Acá la tabla la crea `0011`, en
  el mismo lote, así que está vacía.
- **Compatibilidad hacia atrás.** Todas las columnas nuevas son nullable o tienen default, así que **el código
  viejo sigue funcionando con el esquema nuevo**. Eso importa: permite aplicar las migraciones *antes* de
  desplegar, que es el orden correcto, y permite volver atrás el código sin volver atrás la base.

---

## 3. Azure V2: configuración verificada (sólo lectura)

### Recursos en `rg-turisclick-dev`

| Recurso | Tipo | Región |
|---|---|---|
| `app-turisclick-v2-api` | App Service (`DOTNETCORE\|10.0`) | Brazil South |
| `asp-turisclick-v2-dev` | Plan **F1 Free** | Brazil South |
| `turisclick-postgres-jpm` | PostgreSQL Flexible 16, `Standard_B1ms` Burstable, 32 GB | Brazil South |
| `kv-turisclick-v2-dev` | Key Vault (RBAC, soft delete) | Brazil South |
| `id-turisclick-v2-app` | Identidad administrada de la app | Brazil South |
| `id-turisclick-v2-github-deploy` | Identidad del deploy (OIDC) | Brazil South |
| `swa-turisclick-v2-backoffice` | Static Web Apps Free | East US 2 |

En el mismo servidor PostgreSQL conviven **`turisclick_db` (V1, intocable)** y **`turisclick_db_v2`**. Es la
razón por la que todo script de este repositorio verifica el nombre de la base antes de escribir.

### App settings

| Setting | Valor / forma |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` (Swagger devuelve 404 — correcto) |
| `Ai__Provider` | `Deterministic` |
| `Reservations__Expiration__Enabled` | `true` |
| `Cors__AllowedOrigins__0..2` | `http://localhost:5173`, `http://localhost:8081`, `https://ashy-rock-0dd3b480f.2.azurestaticapps.net` |
| `ConnectionStrings__DefaultConnection` | referencia a Key Vault |
| `Jwt__Key` | referencia a Key Vault |

- **CORS correcto**: el origen del Static Web App desplegado coincide con el host real.
- **Sin secretos en texto**: los dos sensibles son referencias a Key Vault.
- **`alwaysOn: false`** (F1 no lo soporta) — ver sección 5.
- **No hay nada de Duffel**: ni `Flights__Provider` ni el token.

### Key Vault

Tres secretos: `db-connection-string`, `jwt-key`, `seed-admin-password`. La identidad de la app ya tiene
**`Key Vault Secrets User` en el ámbito del vault**, así que un secreto nuevo **no necesita un permiso nuevo**.

---

## 4. Duffel TEST: qué falta

El backend elige el proveedor aéreo en el arranque con `Flights:Provider`, y **el default es `Fake`**. Azure
no tiene ese setting, así que hoy corre con `FakeFlightProvider`: los vuelos "funcionan" pero el inventario es
simulado.

Para activar Duffel en modo TEST hacen falta exactamente dos cosas:

1. `Flights__Provider` = `Duffel`
2. `Flights__Duffel__AccessToken` = referencia a un secreto nuevo de Key Vault (p. ej. `duffel-test-token`)

Y nada más:

- **El permiso ya está.** La identidad de la app lee cualquier secreto del vault.
- **El cinturón de seguridad ya está puesto.** `DuffelOptions.RequireTestToken` es `true` por defecto y
  `DuffelFlightProvider` se **niega a arrancar** con un token que no empiece con `duffel_test_`. No hay que
  configurar nada para que eso pase: habría que configurarlo para desactivarlo.
- **El token no está en ninguna parte del repositorio.** Verificado: no aparece en el código, ni en
  `appsettings*.json`, ni en los workflows, ni en el bundle de Android exportado.
- `Flights__Duffel__TimeoutSeconds` default 30 s. Con F1 y arranque en frío, el primer request después de que
  la app despierta puede quedar cerca de ese límite.

### Cómo se presenta el inventario TEST en la demostración

Los vuelos de Duffel en modo TEST son **ofertas reales de aerolíneas reales con precios reales de la fecha
consultada**, pero las órdenes no existen: no hay pasaje, no hay cobro, no hay nada que volar. La app ya lo
dice sola — la respuesta trae `testMode` y la pantalla muestra el aviso — y en la defensa hay que decirlo con
las mismas palabras: *"el vuelo se busca y se cotiza contra una aerolínea real, en el entorno de pruebas de
Duffel; la reserva aérea es de prueba, igual que el pago"*. No se presenta como una compra.

Lo mismo vale para el pago: es **simulado**, está marcado como simulado en la interfaz, y se presenta así.

---

## 5. Servicios de fondo: el riesgo real

Tres procesos corren dentro del proceso de la API:

| Servicio | Qué hace | Intervalo |
|---|---|---|
| `ReservationExpirationBackgroundService` | expira reservas sin pagar | 60 s |
| `FlightReconciliationBackgroundService` | resuelve emisiones de desenlace desconocido | según `FlightReconciliation` |
| `CancellationResolutionBackgroundService` | completa reembolsos y cancelaciones a medias | 60 s |

**El problema es el plan F1.** No admite `Always On`: sin tráfico, App Service descarga la aplicación. Mientras
está dormida, **ningún timer corre**. Una reserva se expira tarde; una cancelación con el reembolso a medias
se queda a medias hasta que alguien visite el sitio.

Los tres bucles están bien escritos para lo demás: capturan excepciones por pasada y reintentan en la
siguiente, así que una caída momentánea de PostgreSQL o de Duffel no los mata. La resolución de cancelaciones
es idempotente —un reembolso exitoso no se repite, garantizado por un índice único parcial— así que
ejecuciones concurrentes o repetidas no cobran ni devuelven dos veces.

### El cambio operativo más chico que resuelve esto

Hasta esta oleada, el ADMIN podía **ver** la cola de cancelaciones sin resolver (`GET /api/admin/cancellations`)
pero no hacer nada con ella: dependía enteramente del proceso de fondo. En una demostración supervisada eso es
una reserva cancelada con el reembolso colgado y ninguna salida.

Se agregó **`POST /api/admin/cancellations/resolve`**: reintenta la cola ahora, sin esperar al timer. No hace
nada nuevo —llama a la misma resolución que corre sola, que ya era idempotente— y devuelve cuántas había,
cuántas completó y cuántas quedan. Es la salida manual que faltaba.

**Para la demostración supervisada eso alcanza.** Mantener la app despierta visitándola antes de empezar, y
tener el botón de reintento a mano, cubre el riesgo. Lo que **no** es aceptable para producción de verdad:
ahí hay que decidir entre `Always On` (requiere plan pago, no se cambió porque no se pidió) o mover la
resolución a un trabajo agendado fuera del proceso web.

---

## 6. Frontends

### Backoffice

- `VITE_API_TARGET=azure` en `.env.production`; `local` en `.env`. Correcto.
- SPA fallback y cabeceras de seguridad en `public/staticwebapp.config.json`
  (`X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`).
- Despliegue manual con `infra/bootstrap/deploy-backoffice.ps1`, que lee el deployment token de Azure con la
  sesión de `az` y lo pasa por variable de entorno del proceso. No hay token en el repositorio.
- El origen desplegado ya está en el CORS del backend.
- Build de producción: pasa. Un aviso de tamaño de chunk (697 kB), que no bloquea.

### Tourist Mobile

- `eas.json` con tres perfiles (`development`, `preview`, `production`), los tres con
  `EXPO_PUBLIC_API_TARGET=azure`. `preview` genera **APK** con `distribution: internal`, que es lo que sirve
  para la demostración; `production` genera `app-bundle` con `autoIncrement` y `appVersionSource: remote`.
- `expo-secure-store` está en los plugins, así que en el dispositivo los tokens van al Keystore. **En web no
  existe** y la sesión queda sólo en memoria — relevante sólo para QA, no para la app real.
- `expo export --platform android` compila. El bundle **no contiene ningún token de Duffel** (verificado) y
  lleva embebida la URL de Azure.
- **A revisar antes de un build de tienda:** `android.usesCleartextTraffic: true`. Hace falta para el
  desarrollo local contra `http://10.0.2.2:5288`, pero en un build de producción permite tráfico HTTP sin
  cifrar. Para un APK de preview apuntado a HTTPS es aceptable; para la tienda debería ser `false` o
  restringirse con una configuración de seguridad de red.

---

## 7. Base de datos de demostración

`tools/demo/setup-clean-demo.ps1` crea una base aparte, le aplica las migraciones y le carga el catálogo
curado. Resultado verificado sobre `turisclick_v2_demo`:

| | |
|---|---|
| Empresas aprobadas | 8 |
| Experiencias publicadas | 100 |
| Paquetes publicados | 13 |
| Paquetes con política de cancelación | **13 de 13** |
| Destinos con foto | 43 |
| Categorías | 6 (sin residuos de prueba) |
| Fechas futuras de experiencias | 2.000 |
| Salidas futuras de paquetes | 108 |
| Validador del catálogo | **859/859** |

Contra la base de desarrollo el mismo validador falla 6 veces, y ninguna es un problema del producto: son
residuos de fixtures de los tests, que comparten servidor.

El script **se niega** a escribir en `turisclick_db` (V1), en `turisclick_v2_dev`, en `turisclick_v2_test` o
en cualquier nombre que no contenga `demo`, y sólo trabaja contra `localhost`. Borrar exige `-Recreate` y
tipear el nombre de la base. Es idempotente.

> **Importante:** correr los escenarios de validación **escribe** en la base (crea operadores, experiencias,
> paquetes y reservas de prueba). Antes de la demostración hay que reconstruirla con `-Recreate`.

---

## 8. Resultado de las pruebas

| Suite | Antes | Ahora |
|---|---|---|
| Backend (unitarias + integración) | 649 | **651** |
| Tourist Mobile (serial) | 406 | **406** |
| Backoffice | 112 | **184** |
| ESLint móvil | no existía | **0 errores**, 2 advertencias |
| TypeScript móvil | pasa | pasa |
| `expo export` Android | — | compila |
| Lint Backoffice | 0 errores | 0 errores |
| Build de producción Backoffice | pasa | pasa |

Ninguna prueba saltada. Las aserciones que cambiaron lo hicieron porque el texto que verifican cambió a
propósito (formato de plata, redacción de la política de cancelación), no para esconder una falla.

---

## 9. Seguridad

| Chequeo | Resultado |
|---|---|
| Credenciales en archivos versionados | ninguna |
| Token de Duffel en el código, los workflows o el bundle | no aparece |
| Datos reales de pasajeros en fixtures o documentación | no hay; todo sintético |
| Datos de tarjeta | el modelo no los tiene |
| Registro público de operadores | eliminado (`/api/providers/register` no existe) |
| Escalada de privilegios a `PROVIDER` | no hay camino anónimo |
| Aislamiento entre empresas | verificado: 403 al pedir producto ajeno |
| Autorización global del ADMIN | verificada |
| Cambio de contraseña obligatorio | verificado en la **autorización**, no sólo en la UI (403 con la temporal) |
| Dueño del presupuesto de cancelación | el turista no puede ver el libro de otro (403) |
| Idempotencia de reembolsos | índice único parcial; reintentar no duplica |
| Secretos en logs | los dos settings sensibles son referencias a Key Vault; el token de Duffel nunca se loguea |

---

## 10. Bloqueantes

1. **Azure V2 está caído.** Requiere aplicar `0010`–`0014` (o lo que falte) a `turisclick_db_v2`. Es el
   bloqueante real.
2. **La IP de esta máquina no está en el firewall de PostgreSQL**, así que ni el historial de migraciones se
   puede leer ni se pueden aplicar desde acá sin tocar la regla.
3. **Duffel no está configurado en Azure.** Si la demostración incluye vuelos contra inventario real de
   prueba, falta el secreto y los dos settings.
4. **Sin respaldo verificado de `turisclick_db_v2`.** Las migraciones `down` de EF **no son un respaldo**:
   describen cómo deshacer el esquema, no cómo recuperar datos, y varias de ellas borran tablas al revertir.
   Antes de migrar hay que tener un respaldo propio y comprobado.
