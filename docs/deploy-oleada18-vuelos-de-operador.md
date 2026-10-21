# Despliegue: vuelos de operador (Oleada 18) en Azure V2

Estado: **plan, no ejecutado.** Cada paso que toca Azure necesita autorización explícita. Azure V1
(`turisclick_db`, `turisclick-api`) no se toca en ningún paso.

Rama: `integration/turisclick-flights` (B2 + C + A2 sobre `master` 09aa83d). Contratos y comportamiento:
[`vuelos-sueltos-contratos.md`](vuelos-sueltos-contratos.md).

## Qué cambia

| Pieza | Cambio | Despliegue |
|---|---|---|
| API (`app-turisclick-v2-api`) | Buscador global, búsqueda sin sesión con el proveedor simulado, vuelos de operador (catálogo, reserva, pago, emisión), arreglo de expiración | Workflow *Backend V2 CI/CD* |
| Base (`turisclick_db_v2`) | **Migración 0015** (aditiva): `provider_flights`, `provider_flight_departures`, `reservation_passengers`, columnas nuevas en `companies` y `reservation_items`, `ck_reservation_items_product_shape` reemplazado | Script SQL revisado, **antes** del código |
| Backoffice (Static Web App) | Vuelos del operador, emisión, autorización y moderación del admin | `infra/bootstrap/deploy-backoffice.ps1` |
| Tourist Mobile | Buscador, reserva y Mis viajes de vuelos | Expo (no hay build de tienda) |
| Configuración | **Ninguna.** `Flights__Provider` sigue en `Fake` (Terraform `flights_provider = "Fake"`); Duffel apagado | — |

## Por qué la migración va antes que el código

- El workflow **no aplica migraciones**: su verificación `pre` exige que la base tenga exactamente las
  migraciones del código. Con el código nuevo (15) y la base en 14, el deploy se frena en `pre`.
- 0015 es **aditiva y compatible hacia atrás**: el código que corre hoy no lee las tablas nuevas, las columnas
  nuevas tienen default o son nulas, y el CHECK reemplazado acepta todas las filas existentes (verificado
  sobre la base de tests local, poblada con miles de reservas de corridas anteriores). La API actual sigue
  funcionando con 0015 aplicada, así que no hay ventana de incompatibilidad.

## Verificado localmente (nada contra Azure)

- Base aislada nueva: todas las migraciones hasta 0015; rollback a 0014; 0015 de nuevo.
- Base aislada `turisclick_d_e2e` + API integrada: E2E por HTTP 44/44 (dos corridas, cero `[ERR]` en el log).
- Base de tests poblada: 0015 aplicada sobre datos existentes.
- `dotnet ef migrations has-pending-model-changes`: sin cambios.
- Scripts revisables: [`infra/migrations/0015_Oleada18_VuelosDeOperador.up.sql`](../infra/migrations/0015_Oleada18_VuelosDeOperador.up.sql)
  y [`.down.sql`](../infra/migrations/0015_Oleada18_VuelosDeOperador.down.sql), idempotentes
  (`dotnet ef migrations script --idempotent`). El *up* no tiene ningún `DROP TABLE` ni `DROP COLUMN`.

## Procedimiento (cada paso, con autorización)

0. **Integrar en `master` y pushear** (autorización aparte). La build y las pruebas de CI corren solas; no despliegan.

1. **Confirmar qué corre hoy** (sólo lectura) y guardar el paquete de rollback del código, como en la Oleada 15
   ([wave15-readiness.md §4](wave15-readiness.md)): id del deployment activo y copia de `wwwroot` por Kudu con
   el token de `az`. Si el deployment activo cambió desde el último registro, el paquete guardado es el nuevo.

2. **Respaldar `turisclick_db_v2`** con [`infra/bootstrap/backup-v2-database.ps1`](../infra/bootstrap/backup-v2-database.ps1)
   (`pg_dump` custom, sólo esa base, fuera del repo) y verificar el archivo (`pg_restore --list`). Confirmar
   también que el *point-in-time restore* del servidor (7 días) está activo. Es obligatorio: esta vez **sí**
   hay cambio de esquema.

3. **Probar identidad de la base antes de tocarla**: conectado a `turisclick_db_v2` (nunca `turisclick_db`),
   `SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1` → `…0014_Oleada13_AltaDeOperadoresPorAdmin`.

4. **Aplicar 0015** con el script revisado, en una transacción, por la regla de firewall temporal de la
   Oleada 15 (se agrega para la IP del operador y se borra al terminar):
   ```bash
   psql "host=<servidor> dbname=turisclick_db_v2 user=<admin> sslmode=require" \
     --single-transaction -v ON_ERROR_STOP=1 -f infra/migrations/0015_Oleada18_VuelosDeOperador.up.sql
   ```
   Verificación: la última migración es `20261021012403_0015_Oleada18_VuelosDeOperador`; existen
   `provider_flights`, `provider_flight_departures`, `reservation_passengers`; `SELECT count(*) FROM
   reservation_items WHERE issuance_status <> 'NOT_REQUIRED'` → 0. La API actual sigue respondiendo
   (smoke tests de abajo, todos con el código viejo).

5. **Desplegar el backend**: Actions → *Backend V2 CI/CD* → *Run workflow* sobre `master` con `deploy` = ✔,
   `first_deploy_bootstrap` = ✘, `migrations_applied` vacío. El workflow: pruebas → `pre` (la base tiene las
   15) → deploy → smoke tests → `post`.

6. **Desplegar el Backoffice** con `infra/bootstrap/deploy-backoffice.ps1` (build local + SWA).

7. **Smoke tests** (abajo). Después, prueba manual en el teléfono con Expo apuntando a Azure.

## Smoke tests

```bash
API=https://app-turisclick-v2-api.azurewebsites.net
curl -s "$API/api/health"                                                   # 200 {"status":"HEALTHY"}
for p in /api/categories /api/destinations /api/experiences /api/packages; do curl -s -o /dev/null -w "$p %{http_code}\n" "$API$p"; done   # 200
curl -s -o /dev/null -w "airports %{http_code}\n" "$API/api/airports/search?q=madrid"                      # 200
curl -s -X POST "$API/api/flights/search" -H "Content-Type: application/json" \
  -d '{"originIata":"MAD","destinationIata":"VVI","departureDate":"<fecha futura>","adults":1}' | head -c 300   # 200, inventoryMode SIMULATED, sin sesión
curl -s -o /dev/null -w "flights sin token %{http_code}\n" "$API/api/companies/me/flights"                 # 401
curl -s -o /dev/null -w "admin flights sin token %{http_code}\n" "$API/api/admin/flights"                  # 401
curl -s -X POST "$API/api/auth/login" -H "Content-Type: application/json" -d '{"email":"nadie@invalid.local","password":"NoEsUnaCredencial1"}'   # 401
```

Con un token de ADMIN: `GET /api/admin/health/schema` → `turisclick_db_v2`, **15** migraciones, 0 pendientes.
Log (`az webapp log download`): ningún `[ERR]` ni `does not exist` después del arranque.

**Prueba funcional mínima en Azure** (datos de demostración, con autorización aparte porque escribe datos):
el admin autoriza a una empresa de prueba → el operador publica un vuelo → un turista de prueba lo reserva,
paga (simulado) y el operador registra la emisión. Es el mismo recorrido de
[`tools/provider-flight-e2e/run.mjs`](../tools/provider-flight-e2e/run.mjs), que **se niega a correr contra
algo que no sea localhost** a propósito: en Azure se hace a mano y con datos claramente de demo.

## Rollback

| Qué falla | Qué se hace | Base |
|---|---|---|
| Smoke tests o `post` del código nuevo | Redeploy del paquete de rollback guardado en el paso 1 (`az webapp deploy … --clean true`) | **Se deja en 0015**: el código viejo funciona con 0015 (aditiva) |
| Backoffice | Redeploy de la versión anterior de la SWA | — |
| La migración 0015 falla al aplicarse | `--single-transaction` la revierte entera; la base queda en 0014 | Sin cambios |
| Hay que sacar 0015 (último recurso) | `0015_Oleada18_VuelosDeOperador.down.sql` en una transacción, **con el código viejo ya desplegado** | Borra las tablas y columnas de vuelos de operador: **se pierden** sus vuelos, salidas, pasajeros y reservas de vuelo |
| Pérdida de datos | Restaurar el respaldo del paso 2 o el *point-in-time restore* | — |

Bajar 0015 con reservas de vuelo ya hechas también rompe el CHECK de forma (vuelven a existir sólo dos
formas): el `.down.sql` fallaría. Si hubiera reservas FLIGHT, el camino es el respaldo, no el *down*.

## Bloqueantes y riesgos antes de producción

1. **Autorizaciones pendientes**: integrar en `master` y pushear; respaldo; regla de firewall temporal;
   aplicar 0015 en `turisclick_db_v2`; workflow de deploy; deploy del Backoffice; datos de demo en Azure.
2. **No hay emisión real**: el operador registra números de pasaje que emitió fuera de TurisClick. En este
   entorno es demo y así se rotula en las tres apps.
3. **Pago simulado**: cobros y reembolsos son asientos en el libro, sin dinero.
4. **Doble toque al reservar** no se deduplica: dos reservas retienen asientos hasta que una expira (30 min)
   o se cancela.
5. **Duración de los vuelos de operador** no se informa (horarios locales sin husos).
6. **Expiración**: el job de fondo de Azure (`Reservations:Expiration`) libera asientos de vuelos igual que el
   cupo de paquetes; está cubierto por tests de integración, no por el E2E HTTP (el hold dura 30 minutos).
