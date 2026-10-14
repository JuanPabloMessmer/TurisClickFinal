# Oleada 15: recuperación de Azure V2 y preparación del deploy

Escrito el 14 de octubre de 2026. Continúa [deployment-readiness.md](deployment-readiness.md) (Oleada 14), que
cerraba con el backend V2 caído.

**Veredicto: la base V2 está recuperada y el backend actual funciona. El código nuevo está listo para
desplegar, pero no se desplegó: falta autorización.**

---

## 1. Recuperación de la base (13 de octubre de 2026)

### Antes

| Endpoint | Respuesta |
|---|---|
| `GET /api/categories`, `/api/destinations`, `/api/experiences` | 200 |
| `GET /api/packages` | **500** — `42703: column p.cancellation_policy does not exist` |
| `POST /api/auth/login` | **500** — `42703: column u.must_change_password does not exist` |

El texto de PostgreSQL viajaba en el cuerpo de la respuesta a cualquier cliente anónimo (ver sección 2).

### Identidad de la base, probada antes de tocar nada

Conectado como el rol de la app, no como el admin del servidor:

| | |
|---|---|
| `current_database()` | `turisclick_db_v2` |
| `current_user` | `turisclick_v2_app` (no superusuario) |
| Servidor | PostgreSQL 16.15, `turisclick-postgres-jpm` |
| Privilegio `CREATE` sobre `turisclick_db` (V1) | **no** |
| Historial | `0001` a `0009`. **`0010` y `0011` tampoco estaban aplicadas** (la Oleada 14 no lo pudo determinar) |

### Respaldo

- `pg_dump` en formato custom de `turisclick_db_v2` únicamente, con `pg_dump` 16.11.
- Guardado fuera del repositorio, en `%USERPROFILE%\turisclick-backups\wave15\`, carpeta con acceso sólo
  para el usuario: `turisclick_db_v2-pre-0014-20261013-210723.dump` (223.564 bytes, SHA-256
  `0070F4FB…ADDFD0`).
- **Restauración comprobada**: se restauró en una base descartable local y los conteos de las 24 tablas
  coincidieron con Azure (15 usuarios, 16 paquetes, 11 reservas, 2.025 fechas de experiencias, …). La base
  descartable se borró.
- Red de seguridad adicional: backups automáticos del servidor, 7 días de retención.

El procedimiento quedó como [`infra/bootstrap/backup-v2-database.ps1`](../infra/bootstrap/backup-v2-database.ps1).
Es el mismo que se ejecutó, parametrizado con `-Label`; la versión del repositorio no se volvió a correr contra
Azure porque hacerlo abre una regla de firewall.

### Migraciones aplicadas

- Script idempotente `0009 → 0014` generado con `dotnet ef migrations script --idempotent` y revisado: 6
  tablas nuevas, columnas nuevas nullable o con `DEFAULT`, 2 checks, 14 índices creados y 2 reemplazados.
  Cero `DROP TABLE`/`DROP COLUMN`/`DELETE`/`UPDATE`; ningún nombre de base dentro del SQL.
- **Ensayo previo** sobre una copia local del respaldo: aplicó en 211 ms, una segunda pasada no cambió nada.
- **Ejecución**: con `psql -v ON_ERROR_STOP=1`, `lock_timeout=10s`, `statement_timeout=300s`, y antes de
  correr, verificación de la base, del host, del hash del SQL revisado y de que el historial terminara en
  `0009`. Código de salida 0, sin reintentos.

### Después

| Verificación | Resultado |
|---|---|
| Migraciones registradas | **14 de 14**, la última `20261007024736_0014_Oleada13_AltaDeOperadoresPorAdmin` |
| Columnas requeridas / tablas nuevas | 5 de 5 / 6 de 6 |
| Índices inválidos | 0 |
| Datos | sin cambios (15 usuarios, 16 paquetes, 11 reservas) |
| `GET /api/packages` | **200** |
| `POST /api/auth/login` con credenciales falsas | **401** "Email o contraseña inválidos." |
| Logs de la app | los errores `relation … does not exist` de los procesos de fondo terminan a las 01:58:58 UTC; ninguno después |
| Regla de firewall temporal | eliminada; firewall idéntico al estado previo (2 reglas) |
| V1 (`turisclick-api`) | mismas respuestas que antes (login 401, backoffice 401, Swagger 200); las bases del servidor, sin cambios |

No hubo downtime adicional: lo que estaba caído volvió en cuanto terminó la migración, sin redeploy.

---

## 2. Corrección de seguridad: el detalle de los errores

### El problema

`GlobalExceptionHandler` devolvía `exception.Message` en todo 500. En producción eso publicaba a cualquier
cliente anónimo el texto de PostgreSQL, incluido el nombre de columnas y la posición en el SQL. Lo mismo valía
para cualquier excepción no prevista: un mensaje con una connection string o un path interno habría salido
igual.

### La corrección ([GlobalExceptionHandler.cs](../src/TurisClick.Api/Shared/Exceptions/GlobalExceptionHandler.cs))

- Un error no previsto responde **siempre** 500 con un mensaje genérico y un `traceId`. En todos los
  entornos, no sólo en Production: así lo que se prueba es lo que se despliega.
- El detalle completo va al log (`LogError` con la excepción) junto con el mismo `traceId`, así que un error
  reportado por un usuario se encuentra en el log por ese identificador.
- `BadHttpRequestException` (cuerpo ilegible o demasiado grande) conserva su código (400, 413…) pero sin el
  mensaje de Kestrel.
- Los errores de negocio (`NotFound`, `Conflict`, `Forbidden`, `Unauthorized`, `Validation`, `Gone`) siguen
  devolviendo su mensaje y su `errorCode`, ahora también con `traceId`.
- `errorCode` nunca se expone en un 500 (sin cambios).

### Pruebas

- [`GlobalExceptionHandlerTests`](../tests/TurisClick.Api.Tests/Shared/GlobalExceptionHandlerTests.cs) (12):
  un `PostgresException` 42703, un `DbUpdateException` con el SQL adentro y una excepción con una connection
  string en el mensaje no dejan pasar ningún fragmento interno (`42703`, `column`, `SELECT`, `Npgsql`,
  `Host=`, `Password=`, la pila…); el detalle sí llega al log con el mismo `traceId`; cada error de negocio
  conserva su estado y su mensaje.
- [`ErrorDisclosureAndHealthTests`](../tests/TurisClick.Api.Tests/Integration/ErrorDisclosureAndHealthTests.cs)
  (5): el mismo escenario del incidente por el pipeline real de ASP.NET, el 401 del login intacto, y
  `/api/health` (abajo).

---

## 3. Verificación de esquema del deploy

### El problema

El paso de preflight de `backend-v2.yml` decidía buscando `does not exist` en el cuerpo de un 500. Tenía tres
fallas:

1. Dependía del agujero de la sección 2: al cerrarlo, cualquier base atrasada habría pasado.
2. Un 500 por cualquier otro motivo se aceptaba.
3. Sondeaba el código **ya desplegado**, no el que se iba a desplegar: no sabía qué migraciones necesitaba
   el commit nuevo.

### `GET /api/health`

[HealthController](../src/TurisClick.Api/Infrastructure/Health/HealthController.cs), anónimo:

```json
{ "status": "HEALTHY", "database": "turisclick_db_v2", "appliedMigrations": 14,
  "latestMigration": "20261007024736_0014_Oleada13_AltaDeOperadoresPorAdmin", "pendingMigrations": 0 }
```

- `database` sale de `SELECT current_database()`: lo responde el servidor, no la connection string.
- `appliedMigrations`/`latestMigration` salen de `__EFMigrationsHistory`; `pendingMigrations` es lo que el
  código que corre declara y la base no tiene.
- 200 sólo si `HEALTHY`. `PENDING_MIGRATIONS` y `DATABASE_UNAVAILABLE` responden 503. Si la base no responde
  no se dice por qué.
- No expone host, usuario, puerto ni ningún dato. Sí expone el nombre de la base y de las migraciones: es
  metadata del esquema, aceptada a cambio de que el pipeline pueda verificar sin credenciales de base.

### [`.github/scripts/schema_preflight.py`](../.github/scripts/schema_preflight.py)

Python estándar, sin dependencias. Las decisiones son funciones puras con 27 pruebas
([`test_schema_preflight.py`](../.github/scripts/test_schema_preflight.py)) que corren en el job de build.

| Verificación | `pre` (antes) | `post` (después) |
|---|---|---|
| La URL es `app-turisclick-v2-api.azurewebsites.net` | ✔ | ✔ |
| `ConnectionStrings__DefaultConnection` es exactamente la referencia a `db-connection-string` de `kv-turisclick-v2-dev` (el valor nunca se imprime) | ✔ | ✔ |
| `/api/health` dice `turisclick_db_v2` (si dice `turisclick_db`, aborta como V1) | ✔ | ✔ |
| La base tiene **exactamente** las migraciones del commit que se despliega (leídas de sus `[Migration]`) | ✔ | ✔ |
| El código desplegado no ve migraciones pendientes | | ✔ |
| `/api/packages` responde 200 y el login falso 401; **cualquier 500 falla** | ✔ | ✔ |

Ningún paso lee el cuerpo de un 500.

**Por única vez**: la app que hoy corre en Azure es anterior a `/api/health` y responde 404. En la fase `pre`,
y sólo con un 404, el workflow acepta la migración declarada a mano en `migrations_applied`, que tiene que ser
la última del código (`0014`), y lo marca como advertencia. La fase `post` verifica la base de verdad con el
código nuevo. Desde el segundo deploy esto ya no se usa.

Ejecutado contra Azure en modo sólo lectura el 14 de octubre: sin declarar → falla; `0013` → falla; `0014` →
pasa con advertencia. Las sondas dieron 200 y 401.

---

## 4. Estado del repositorio

- Rama: `claude/turisclick-wave-15-azure-v2-5f3e9d`, sobre `master` en `3a17577` (Oleada 14). **No está
  pusheada ni mergeada.**
- `origin/main` es una historia vieja y sin relación con `master` (sin ancestro común). La rama de trabajo se
  basa en `master`, que es la que despliega el workflow.

### Archivos

| Archivo | Cambio |
|---|---|
| `src/TurisClick.Api/Shared/Exceptions/GlobalExceptionHandler.cs` | 500 genérico con `traceId`; `BadHttpRequestException` |
| `src/TurisClick.Api/Infrastructure/Health/HealthController.cs` | nuevo: `GET /api/health` |
| `tests/TurisClick.Api.Tests/Shared/GlobalExceptionHandlerTests.cs` | nuevo, 12 pruebas |
| `tests/TurisClick.Api.Tests/Integration/ErrorDisclosureAndHealthTests.cs` | nuevo, 5 pruebas |
| `.github/scripts/schema_preflight.py`, `test_schema_preflight.py` | nuevos |
| `.github/workflows/backend-v2.yml` | preflight nuevo antes y después del deploy; corre las pruebas del script |
| `infra/bootstrap/backup-v2-database.ps1` | nuevo: respaldo verificado de V2 |
| `docs/wave15-readiness.md`, `docs/azure-v2-deployment-plan.md`, `docs/deployment-readiness.md` | documentación |

### Pruebas

| Suite | Oleada 14 | Ahora |
|---|---|---|
| Backend (unitarias + integración), Release | 651 | **668**, 0 fallas, 0 saltadas |
| Verificación de esquema (Python) | — | **27** |
| Tourist Mobile / Backoffice | 406 / 184 | sin cambios de código; no se volvieron a correr |

Build Release: 0 errores. Las 2 advertencias (`xUnit2031` en `ReservationCancellationRefundTests.cs`) ya
existían.

---

## 5. Problemas conocidos

1. **Mensajes de Duffel en errores de negocio.** Cuando la emisión de un vuelo falla, el 409 lleva el texto
   que devuelve Duffel (`FlightBookingOrchestrator` → `ConflictAppException`). No es información interna
   nuestra, pero es texto de un tercero sin filtrar. Fuera de alcance de esta oleada.
2. **`cancellationPolicy` vacía en Azure.** Los 16 paquetes que ya existían tienen la columna nueva en `NULL`.
   La carga del catálogo de demostración la completa.
3. **Plan F1**: sin `Always On`, los procesos de fondo se detienen cuando la app duerme (ver la Oleada 14).
4. **Rollback del código.** El workflow despliega sólo `master`; volver atrás es revertir y relanzar. Por eso
   el procedimiento de abajo guarda primero el paquete que corre hoy.
5. `/api/health` expone el nombre de la base y de las migraciones (decisión explicada en la sección 3).

---

## 6. Procedimiento de deploy del backend (no ejecutado)

Cada paso necesita autorización explícita.

1. **Guardar el paquete que corre hoy** (sólo lectura, para el rollback): descargar `site/wwwroot` como zip
   desde Kudu (`https://app-turisclick-v2-api.scm.azurewebsites.net/api/zip/site/wwwroot/`, o el portal →
   *Advanced Tools*) y guardarlo en `%USERPROFILE%\turisclick-backups\`. El modo de autenticación contra Kudu
   desde la CLI no se probó en esta oleada: confirmarlo antes de depender de este paso.
2. **Integrar la rama en `master` y pushear** (`git push origin master`). La build y las pruebas corren solas;
   no despliegan.
3. **Lanzar el workflow**: Actions → *Backend V2 CI/CD* → *Run workflow* sobre `master`, `deploy` marcado,
   `migrations_applied` = `0014`.
4. El workflow, en orden: pruebas del script y del backend → **`pre`** (advertencia esperada por única vez:
   la app vieja no tiene `/api/health`) → empaquetar → `az webapp deploy` → smoke tests → **`post`**.

### Smoke tests después del deploy

```bash
API=https://app-turisclick-v2-api.azurewebsites.net
curl -s "$API/api/health"                       # 200, HEALTHY, turisclick_db_v2, 14, pendingMigrations 0
for p in /api/categories /api/destinations /api/experiences /api/packages; do curl -s -o /dev/null -w "$p %{http_code}\n" "$API$p"; done   # 200
curl -s -X POST "$API/api/auth/login" -H "Content-Type: application/json" -d '{"email":"nadie@invalid.local","password":"NoEsUnaCredencial1"}'   # 401 con traceId
curl -s -o /dev/null -w "%{http_code}\n" "$API/swagger/v1/swagger.json"   # 404
```

Y en el log (`az webapp log download`): ningún `fail:` ni `does not exist` después del arranque.

### Rollback

- **El esquema no cambia en este deploy**, así que volver al código anterior no requiere tocar la base.
- Opción 1: redeplegar el zip guardado en el paso 1 (`az webapp deploy --type zip --src-path …`).
- Opción 2: `git revert` en `master` y relanzar el workflow. La fase `pre` va a ver `/api/health` del código
  nuevo y 14 migraciones, que coinciden con las del commit revertido.
- Criterio: cualquier 500 en los smoke tests, `post` en rojo o errores nuevos en el log.

### ¿Hace falta otro respaldo de la base?

**No es obligatorio**: este deploy no aplica migraciones ni escribe datos, y el respaldo del 13 de octubre
más el *point-in-time restore* del servidor (7 días) cubren el riesgo. Sí hace falta uno nuevo antes de
cargar el catálogo de demostración, que escribe datos.

---

## 7. Autorizaciones pendientes

1. Push e integración en `master`.
2. Guardar el paquete actual y lanzar el workflow de deploy del backend.
3. Después del backend, en este orden, cada una por separado: catálogo de demostración (con respaldo previo),
   Duffel TEST, Backoffice, APK de Android.
