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
  (11): el mismo escenario del incidente por el pipeline real de ASP.NET, el 401 del login intacto, y
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

### Salud: pública mínima, detalle sólo para ADMIN

Revisado antes del deploy: la primera versión de `/api/health` publicaba el nombre de la base y los ids de
las migraciones a cualquiera. No eran secretos (están en el repositorio), pero decían a un anónimo si la base
de producción estaba atrasada. Quedó así:

| Endpoint | Acceso | Responde |
|---|---|---|
| `GET /api/health` | anónimo | `{"status":"HEALTHY"}` (200) o `{"status":"UNHEALTHY"}` (503). Nada más. |
| `GET /api/health?expect=<huella>` | anónimo | lo mismo más `"schemaMatches": true/false` |
| `GET /api/admin/health/schema` | **ADMIN** | base real, migraciones aplicadas/pendientes, última, huella |

- **Sana** = la base responde, el código no ve migraciones pendientes, y la base es la esperada:
  `appsettings.Production.json` (va dentro del paquete) fija `Database:ExpectedName = turisclick_db_v2`. Si la
  app de producción llegara a conectarse a otra base —V1 incluida—, deja de estar sana.
- **La huella** es SHA-256 de `turisclick_db_v2` y de las migraciones en orden, una por línea. La app la
  calcula con `current_database()` (lo responde el servidor) y `__EFMigrationsHistory`; el pipeline, con las
  migraciones del commit que despliega. Si coinciden, la base es V2 **y** tiene exactamente esas migraciones.
  La respuesta no nombra nada: sólo verdadero o falso. La comparación es de tiempo constante.
- La implementación es la misma en C# ([DatabaseSchemaInspector](../src/TurisClick.Api/Infrastructure/Health/DatabaseSchemaInspector.cs))
  y en Python (`fingerprint` en el script); un vector fijo, repetido en las pruebas de los dos lados, impide que
  diverjan.
- Lo que un anónimo todavía puede hacer es adivinar huellas. Le confirmaría un dato que ya está en el
  repositorio; no le da ninguna capacidad nueva.

Ensayado el 14 de octubre con el paquete nuevo en `Production` contra una réplica local de la base de Azure
(respaldo + migraciones 0010–0014, con el nombre `turisclick_db_v2`): `/api/health` → `{"status":"HEALTHY"}`;
huella del pipeline → `schemaMatches: true`; otra huella → `false`; el detalle anónimo → 401; catálogo 200,
login 401 con `traceId`; ningún error en el log.

### [`.github/scripts/schema_preflight.py`](../.github/scripts/schema_preflight.py)

Python estándar, sin dependencias, con 40 pruebas ([`test_schema_preflight.py`](../.github/scripts/test_schema_preflight.py))
que corren en el job de build. Incluyen pruebas sobre el propio `backend-v2.yml`.

| Verificación | `pre` (antes) | `post` (después) |
|---|---|---|
| La URL es `app-turisclick-v2-api.azurewebsites.net` | ✔ | ✔ |
| `ConnectionStrings__DefaultConnection` es exactamente la referencia a `db-connection-string` de `kv-turisclick-v2-dev` (el valor nunca se imprime) | ✔ | ✔ |
| `schemaMatches` es verdadero para la huella de `turisclick_db_v2` con las migraciones del commit | ✔ | ✔ |
| La app está sana (`HEALTHY`, 200) | ✔ | ✔ |
| `/api/packages` responde 200 y el login falso 401; **cualquier 500 falla** | ✔ | ✔ |

Ningún paso lee el cuerpo de un error.

### Excepción de primer deploy

La app que corre hoy (build de `e8d0756`) no tiene `/api/health` y responde 404. Sin una excepción, ningún
deploy podría pasar `pre`. La excepción se acepta **sólo** si se cumple todo esto:

1. fase `pre` y `/api/health` respondió exactamente 404 (un 500, un 503 o un timeout no la habilitan);
2. se marcó el input `first_deploy_bootstrap` (por defecto `false`);
3. el deployment activo en Azure, leído por ARM, es **`e66e32a0-6106-469e-b9d3-e55d9cbc8ab9`**: el paquete exacto
   que se guardó y ensayó como rollback;
4. el código a desplegar tiene exactamente **14 migraciones, la última `0014`**: agregar una migración la apaga;
5. `migrations_applied` es el **id completo** de esa migración, verificado por fuera (el número corto no alcanza).

Y además: si se pide cuando la app ya tiene `/api/health`, el paso **falla** (no se usa "por las dudas"); en
`post` no existe (el script falla si se la pasan). Las condiciones 3 y 4 hacen que deje de aplicar sola: tras el
primer deploy el deployment activo es otro y la app ya responde.

**La verificación post es obligatoria.** Corre con `if: always()` siempre que el paso de deploy haya llegado a
ejecutarse —también si fallaron los smoke tests o el deploy mismo—, propaga su código de salida, y no hay
`continue-on-error` en el workflow. Lo comprueban las pruebas del script.

Ejecutado contra Azure en modo sólo lectura el 14 de octubre, con el deployment activo leído por ARM:

| Lanzamiento | Resultado |
|---|---|
| sin la excepción | falla |
| excepción con `0014` | falla (id incompleto) |
| excepción con otro deployment | falla |
| excepción completa | pasa, con advertencia |

Las sondas dieron 200 y 401 en todos los casos.

---

## 4. Rollback

### Qué corre hoy, guardado

| | |
|---|---|
| Deployment activo | `e66e32a0-6106-469e-b9d3-e55d9cbc8ab9`, OneDeploy, 7 de octubre de 2026 23:02 UTC |
| Commit | **`e8d0756`** (`ProductVersion 1.0.0+e8d0756…`), Oleada 13 |
| Copia completa de `wwwroot` | `%USERPROFILE%\turisclick-backups\wave15\rollback\app-v2-wwwroot-e66e32a0.zip`, SHA-256 `dcaab5d8…aa674`, 60 archivos |
| **Paquete de rollback** | `…\rollback\app-v2-rollback-e8d0756.zip`, SHA-256 `5bfc4aef…ec09e`, 39 archivos |

Cómo se obtuvo, sólo lectura: Kudu acepta el token de Entra ID de la sesión de `az` (la autenticación básica
de SCM está deshabilitada, como debe):

```bash
TOKEN=$(az account get-access-token --resource https://management.azure.com/ --query accessToken -o tsv)
curl -sf -H "Authorization: Bearer $TOKEN" -o app-v2-wwwroot.zip https://app-turisclick-v2-api.scm.azurewebsites.net/api/zip/site/wwwroot/
```

La copia completa trae 20 archivos `logs/turisclick-*.log` que la app escribe dentro de `wwwroot`. Se revisaron:
sin contraseñas, tokens ni direcciones de correo. El paquete de rollback es la misma copia **sin** `logs/` ni
`hostingstart.html`. La carpeta tiene acceso sólo para el usuario y está fuera del repositorio.

**Ensayado**: el paquete se ejecutó localmente en `Production` contra una réplica exacta de la base de hoy
(respaldo + 0010–0014). Categorías, destinos, experiencias y paquetes 200; login 401; `/api/health` 404 (es
código anterior); ningún error en el log.

### Procedimiento (no ejecutado)

Cuándo: cualquier 500 en los smoke tests, la verificación `post` en rojo, o errores nuevos en el log.

```bash
az webapp deploy -g rg-turisclick-dev -n app-turisclick-v2-api \
  --src-path "$USERPROFILE/turisclick-backups/wave15/rollback/app-v2-rollback-e8d0756.zip" \
  --type zip --clean true --restart true
```

Verificación:

```bash
API=https://app-turisclick-v2-api.azurewebsites.net
for p in /api/categories /api/destinations /api/experiences /api/packages; do curl -s -o /dev/null -w "$p %{http_code}\n" --max-time 120 "$API$p"; done   # 200
curl -s -o /dev/null -w "login %{http_code}\n" -X POST "$API/api/auth/login" -H "Content-Type: application/json" -d '{"email":"nadie@invalid.local","password":"NoEsUnaCredencial1"}'   # 401
curl -s -o /dev/null -w "health %{http_code}\n" "$API/api/health"   # 404: volvió el código de e8d0756
```

- **La base no se toca**: este deploy no cambia el esquema, y el código viejo funciona con las 14 migraciones.
- **No se hace rollback relanzando el workflow con un commit revertido**: el código revertido no tiene
  `/api/health` y la verificación `post` fallaría a propósito.
- Después de un rollback, el deployment activo ya no es `e66e32a0…`, así que la excepción de primer deploy deja
  de aplicar. Volver a intentar el deploy exige actualizar `FIRST_DEPLOY` en `schema_preflight.py` en un commit
  revisado, con el id nuevo. Es intencional: cada uso de la excepción queda en el historial.
- `--clean true` borra `wwwroot/logs`. Los logs hasta hoy están en la copia completa.

---

## 5. Estado del repositorio

- Rama: `claude/turisclick-wave-15-azure-v2-5f3e9d`, sobre `master` en `3a17577` (Oleada 14). **No está
  pusheada ni mergeada.**
- `origin/main` es una historia vieja y sin relación con `master` (sin ancestro común). La rama de trabajo se
  basa en `master`, que es la que despliega el workflow.

### Archivos

| Archivo | Cambio |
|---|---|
| `src/TurisClick.Api/Shared/Exceptions/GlobalExceptionHandler.cs` | 500 genérico con `traceId`; `BadHttpRequestException` |
| `src/TurisClick.Api/Infrastructure/Health/HealthController.cs` | `GET /api/health` (mínimo) y `GET /api/admin/health/schema` (ADMIN) |
| `src/TurisClick.Api/Infrastructure/Health/DatabaseSchemaInspector.cs` | base real, migraciones y huella |
| `src/TurisClick.Api/appsettings.Production.json` | `Database:ExpectedName = turisclick_db_v2` |
| `src/TurisClick.Api/Program.cs` | registra el inspector |
| `tests/TurisClick.Api.Tests/Shared/GlobalExceptionHandlerTests.cs` | 12 pruebas |
| `tests/TurisClick.Api.Tests/Integration/ErrorDisclosureAndHealthTests.cs` | 11 pruebas |
| `tests/TurisClick.Api.Tests/Infrastructure/SchemaFingerprintTests.cs` | 3 pruebas (vector compartido con Python) |
| `.github/scripts/schema_preflight.py`, `test_schema_preflight.py` | verificación de esquema, 40 pruebas |
| `.github/workflows/backend-v2.yml` | `pre` y `post`; excepción de primer deploy; `post` obligatorio |
| `infra/bootstrap/backup-v2-database.ps1` | respaldo verificado de V2 |
| `docs/wave15-readiness.md`, `docs/azure-v2-deployment-plan.md`, `docs/deployment-readiness.md` | documentación |

### Pruebas

| Suite | Oleada 14 | Ahora |
|---|---|---|
| Backend (unitarias + integración), Release | 651 | **677**, 0 fallas, 0 saltadas |
| Verificación de esquema y workflow (Python) | — | **40** |
| Tourist Mobile / Backoffice | 406 / 184 | sin cambios de código; no se volvieron a correr |

Build Release: 0 errores. Las 2 advertencias (`xUnit2031` en `ReservationCancellationRefundTests.cs`) ya
existían.

---

## 6. Problemas conocidos

1. **Mensajes de Duffel en errores de negocio.** Cuando la emisión de un vuelo falla, el 409 lleva el texto
   que devuelve Duffel (`FlightBookingOrchestrator` → `ConflictAppException`). No es información interna
   nuestra, pero es texto de un tercero sin filtrar.
2. **`cancellationPolicy` vacía en Azure.** Los 16 paquetes que ya existían tienen la columna nueva en `NULL`.
   La carga del catálogo de demostración la completa.
3. **Plan F1**: sin `Always On`, los procesos de fondo se detienen cuando la app duerme.
4. **Logs de archivo dentro de `wwwroot`.** Serilog escribe `logs/turisclick-*.log` en el directorio de la app:
   cualquiera con acceso de deploy los descarga junto con el código, y un deploy con limpieza los borra. Sin
   datos sensibles hoy (revisado), pero en producción conviene dejar sólo la consola (que App Service ya
   recoge).
5. **El permiso del pipeline para leer el deployment activo no se probó con su identidad.** La lectura por ARM
   funciona con la cuenta del usuario y `Website Contributor` incluye `Microsoft.Web/sites/*`. Si fallara, el
   id queda vacío y la excepción se rechaza: el deploy se **bloquea**, no se saltea la verificación.
6. Un anónimo puede adivinar huellas de esquema (sección 3).

---

## 7. Procedimiento de deploy del backend (no ejecutado)

Cada paso necesita autorización explícita.

1. **Confirmar que el deployment activo sigue siendo `e66e32a0…`** (sólo lectura; si cambió, el paquete de
   rollback ya no es lo que corre y hay que repetir la sección 4):
   ```bash
   az rest --method get --url "https://management.azure.com/subscriptions/fd5eebd9-75df-4fd3-b733-f32f60c50be2/resourceGroups/rg-turisclick-dev/providers/Microsoft.Web/sites/app-turisclick-v2-api/deployments?api-version=2023-12-01" --query "value[?properties.active].name" -o tsv
   ```
2. **Integrar la rama en `master` y pushear.** La build y las pruebas corren solas; no despliegan.
3. **Lanzar el workflow**: Actions → *Backend V2 CI/CD* → *Run workflow* sobre `master` con
   - `deploy` = ✔
   - `first_deploy_bootstrap` = ✔
   - `migrations_applied` = `20261007024736_0014_Oleada13_AltaDeOperadoresPorAdmin`
4. El workflow: pruebas → `pre` (advertencia esperada: primer deploy) → empaquetar → `az webapp deploy` →
   smoke tests → `post` (obligatorio).

Desde el segundo deploy: `first_deploy_bootstrap` desmarcado y `migrations_applied` vacío.

### Smoke tests después del deploy

```bash
API=https://app-turisclick-v2-api.azurewebsites.net
curl -s "$API/api/health"                                                   # 200 {"status":"HEALTHY"}
for p in /api/categories /api/destinations /api/experiences /api/packages; do curl -s -o /dev/null -w "$p %{http_code}\n" "$API$p"; done   # 200
curl -s -X POST "$API/api/auth/login" -H "Content-Type: application/json" -d '{"email":"nadie@invalid.local","password":"NoEsUnaCredencial1"}'   # 401 con traceId
curl -s -o /dev/null -w "%{http_code}\n" "$API/api/admin/health/schema"    # 401 sin token
curl -s -o /dev/null -w "%{http_code}\n" "$API/swagger/v1/swagger.json"    # 404
```

Con un token de ADMIN, `GET /api/admin/health/schema` → `turisclick_db_v2`, 14 migraciones, 0 pendientes. En el
log (`az webapp log download`): ningún `[ERR]` ni `does not exist` después del arranque.

### ¿Hace falta otro respaldo de la base?

**No es obligatorio**: este deploy no aplica migraciones ni escribe datos, y el respaldo del 13 de octubre
más el *point-in-time restore* del servidor (7 días) cubren el riesgo. Sí hace falta uno nuevo antes de
cargar el catálogo de demostración, que escribe datos.

---

## 8. Recomendación

**GO para desplegar el backend**, sujeto a las autorizaciones de abajo.

- La base está recuperada y verificada; el deploy no la modifica.
- El rollback está preparado y **ensayado**: el paquete exacto que corre hoy, identificado por commit, guardado
  y ejecutado contra una réplica de la base.
- El código nuevo está ensayado en `Production` contra la misma réplica.
- La verificación `pre` está probada contra Azure en modo lectura; la excepción de primer deploy está atada a
  ese paquete y a esas migraciones; la `post` es obligatoria.

Condición de parada: si el paso 1 muestra otro deployment activo, **no** desplegar hasta repetir la sección 4.

## 9. Autorizaciones pendientes

1. Push e integración en `master`.
2. Lanzar el workflow de deploy del backend con los inputs de la sección 7.
3. Después del backend, cada una por separado: catálogo de demostración (con respaldo previo), Duffel TEST,
   Backoffice, APK de Android.
