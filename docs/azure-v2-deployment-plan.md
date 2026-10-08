# Plan de despliegue de TurisClick V2 a Azure

Procedimiento ordenado, con una condición de verificación por paso. Cada paso dice cómo saber que salió bien
antes de pasar al siguiente; si la verificación falla, se va a la sección 18 y no se sigue.

**Nada de este plan se ejecutó.** El estado actual está en
[deployment-readiness.md](deployment-readiness.md), y hoy el backend V2 está caído.

## Reglas que no se negocian

- **Nunca se aplican migraciones de V2 a `turisclick_db`.** Esa es la base de V1. Las dos conviven en el mismo
  servidor PostgreSQL, así que el nombre de la base se verifica en cada paso que escribe.
- **Nunca se usa el modo Live de Duffel.** Sólo `duffel_test_`.
- **Las migraciones se aplican ANTES de desplegar el código.** Todas las columnas nuevas son nullable o tienen
  default, así que el código viejo funciona con el esquema nuevo. Al revés no: el código nuevo contra el
  esquema viejo es exactamente lo que dejó el sitio caído.
- **Un secreto nunca se escribe en `appsettings`, en el repositorio, en una variable del pipeline ni en un
  log.** Van a Key Vault y se referencian.

---

## 1. Chequeos previos

```bash
git status --porcelain          # vacío
git log --oneline -1           # el commit que se va a desplegar
dotnet test                    # 651/651
```

```bash
npm --prefix frontend/apps/backoffice run build
```

```bash
cd frontend/apps/tourist-mobile && npx jest --runInBand && npx tsc --noEmit && npx eslint .
```

**Verificación:** árbol limpio, 651 pruebas de backend, 406 de móvil, 184 de backoffice, 0 errores de lint,
las dos builds compilan.

---

## 2. Verificar el destino en Azure

```bash
az account show --query "{suscripcion:name, id:id}" -o json
az webapp show -g rg-turisclick-dev -n app-turisclick-v2-api --query "{estado:state, runtime:siteConfig.linuxFxVersion}" -o json
az postgres flexible-server db list -g rg-turisclick-dev -s turisclick-postgres-jpm --query "[].name" -o tsv
```

**Verificación:** la suscripción es `fd5eebd9-75df-4fd3-b733-f32f60c50be2`, la web app está `Running` con
`DOTNETCORE|10.0`, y la lista de bases incluye `turisclick_db_v2`. Si en algún momento de lo que sigue aparece
`turisclick_db` como destino, **se detiene todo**.

Habilitar la IP de la máquina que va a migrar (es un cambio de infraestructura: hacerlo a conciencia y
quitarlo al terminar):

```bash
az postgres flexible-server firewall-rule create -g rg-turisclick-dev -s turisclick-postgres-jpm --rule-name migracion-temporal --start-ip-address <TU_IP> --end-ip-address <TU_IP>
```

**Verificación:** `psql` conecta a `turisclick_db_v2` y `SELECT current_database();` devuelve ese nombre.

---

## 3. Respaldo y preparación de la recuperación

Esto es el paso que no se saltea. Las migraciones `down` de EF **no son un respaldo**: describen cómo deshacer
el esquema, y varias borran tablas al revertir. No recuperan datos.

```bash
az postgres flexible-server backup list -g rg-turisclick-dev -n turisclick-postgres-jpm -o table
```

**Verificación:** hay un backup automático reciente y se conoce la ventana de *point-in-time restore* del
servidor. Además, un volcado lógico propio de la base V2 únicamente:

```bash
pg_dump --host=turisclick-postgres-jpm.postgres.database.azure.com --username=<usuario> --dbname=turisclick_db_v2 --format=custom --file=turisclick_db_v2-antes-de-0014.dump
```

**Verificación:** el archivo existe, pesa más de cero, y `pg_restore --list` lo lee sin error. Guardarlo fuera
del repositorio.

> Si el volcado no se puede hacer o no se puede verificar, **no se sigue**.

---

## 4. Revisar el SQL de las migraciones pendientes

Primero, el estado real:

```sql
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
```

**Verificación:** anotar la última migración aplicada. Debería estar entre `0009` y `0011`.

Generar el script idempotente desde esa migración hasta `0014`:

```bash
dotnet ef migrations script <ULTIMA_APLICADA> 0014_Oleada13_AltaDeOperadoresPorAdmin --project src/TurisClick.Api --idempotent --output pendientes.sql
```

Leer `pendientes.sql` entero y confirmar:

- no hay `DROP TABLE`, `DROP COLUMN`, `DELETE` ni `TRUNCATE`;
- toda columna `NOT NULL` nueva trae `DEFAULT`;
- los `DROP INDEX` están acompañados del `CREATE` que los reemplaza;
- cada migración va dentro de su propio `START TRANSACTION` / `COMMIT` (son 5, uno por migración: una falla a
  mitad de camino deja aplicadas las anteriores, y se retoma volviendo a correr el mismo script).

**Verificación:** el resumen del script coincide con la tabla de
[deployment-readiness.md](deployment-readiness.md#2-inventario-de-migraciones). Si aparece una operación que
no está ahí, **se detiene y se revisa** antes de ejecutar nada.

---

## 5. Aplicar las migraciones a V2, y sólo a V2

```bash
psql "host=turisclick-postgres-jpm.postgres.database.azure.com port=5432 dbname=turisclick_db_v2 user=<usuario> sslmode=require" -c "SELECT current_database();"
```

**Verificación:** devuelve exactamente `turisclick_db_v2`. Recién entonces:

```bash
psql "host=turisclick-postgres-jpm.postgres.database.azure.com port=5432 dbname=turisclick_db_v2 user=<usuario> sslmode=require" -v ON_ERROR_STOP=1 -f pendientes.sql
```

**Verificación:**

```sql
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1;
-- 20261007024736_0014_Oleada13_AltaDeOperadoresPorAdmin

SELECT count(*) FROM information_schema.columns
WHERE (table_name, column_name) IN (('packages','cancellation_policy'), ('users','must_change_password'), ('packages','includes_flight'));
-- 3
```

El sitio sigue caído en este punto: el esquema ya está bien, pero el código desplegado es el mismo de antes y
ahora sí es compatible. Comprobarlo:

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://app-turisclick-v2-api.azurewebsites.net/api/packages
curl -s -o /dev/null -w "%{http_code}\n" -X POST https://app-turisclick-v2-api.azurewebsites.net/api/auth/login -H "Content-Type: application/json" -d '{"email":"nadie@invalid.local","password":"NoEsUnaCredencial1"}'
```

**Verificación:** `/api/packages` responde **200** y el login responde **401** (credenciales inválidas), no
500. **Con esto el incidente queda resuelto, antes incluso de desplegar nada.**

---

## 6. Configurar los secretos de Key Vault

Sólo si la demostración va a usar vuelos con inventario real de prueba.

```bash
az keyvault secret set --vault-name kv-turisclick-v2-dev --name duffel-test-token --value "<TOKEN>" --output none
```

El token se tipea en ese momento, se usa `--output none` para que no se imprima, y **tiene que empezar con
`duffel_test_`**.

```bash
az webapp config appsettings set -g rg-turisclick-dev -n app-turisclick-v2-api --settings \
  "Flights__Provider=Duffel" \
  "Flights__Duffel__AccessToken=@Microsoft.KeyVault(VaultName=kv-turisclick-v2-dev;SecretName=duffel-test-token)" \
  --output none
```

**Verificación:**

```bash
az keyvault secret list --vault-name kv-turisclick-v2-dev --query "[].name" -o tsv   # incluye duffel-test-token
az webapp config appsettings list -g rg-turisclick-dev -n app-turisclick-v2-api --query "[?name=='Flights__Provider'].value" -o tsv  # Duffel
```

Nunca se imprime el valor del secreto.

---

## 7. Verificar el acceso de la identidad administrada

```bash
az role assignment list --assignee 8a01a8a3-235f-43b4-b8f5-908fe0e4e257 --all --query "[].{rol:roleDefinitionName, alcance:scope}" -o table
```

**Verificación:** aparece `Key Vault Secrets User` con alcance el vault. Ya está así, por lo que el secreto
nuevo no necesita permisos nuevos.

Y que las referencias resuelvan de verdad:

```bash
az webapp restart -g rg-turisclick-dev -n app-turisclick-v2-api
az webapp log tail -g rg-turisclick-dev -n app-turisclick-v2-api
```

**Verificación:** la app arranca sin errores de configuración. Si el token no fuera de prueba,
`DuffelFlightProvider` se niega a arrancar a propósito: ese error en el log es la red funcionando, no un
problema que haya que saltear.

---

## 8. Desplegar el backend

Desde GitHub Actions, **a mano**: Actions → *Backend V2 CI/CD* → *Run workflow*, marcando `deploy` y anotando
en `migrations_applied` la migración que quedó aplicada en el paso 5.

El workflow, en orden: build y pruebas → **verifica el esquema contra el backend desplegado** → empaqueta →
despliega → smoke tests. Si la base estuviera atrasada, se detiene en la verificación sin tocar la app.

**Verificación:** el job termina en verde y el paso "Verificar que la base V2 tiene las migraciones del
código" dice `la base V2 responde a las columnas que el código espera`.

---

## 9. Smoke tests del backend

```bash
API=https://app-turisclick-v2-api.azurewebsites.net
for p in /api/categories /api/destinations /api/experiences /api/packages; do
  printf "%-22s " "$p"; curl -s -o /dev/null -w "%{http_code}\n" --max-time 120 "$API$p"
done
```

**Verificación:** los cuatro devuelven 200. El primero puede tardar: F1 no tiene `Always On` y la aplicación
arranca en frío.

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$API/api/auth/login" -H "Content-Type: application/json" -d '{"email":"nadie@invalid.local","password":"NoEsUnaCredencial1"}'
curl -s -o /dev/null -w "%{http_code}\n" "$API/swagger/v1/swagger.json"
```

**Verificación:** login **401** (no 500), Swagger **404** (está en `Production`, correcto).

---

## 10. Cargar el catálogo de demostración

```powershell
$env:TURISCLICK_API = 'https://app-turisclick-v2-api.azurewebsites.net'
.\tools\demo-catalog\run-catalog.ps1 load-catalog.mjs
```

El cargador es idempotente y usa sólo endpoints normales de la API: da de alta las 8 empresas por el camino
del administrador, publica 100 experiencias y 13 paquetes con su política de cancelación, abre el calendario
—relativo a hoy, no fechas fijas— y aplica las 43 fotos de destino.

**Verificación:**

```powershell
.\tools\demo-catalog\run-catalog.ps1 validate-catalog.mjs
```

Tiene que decir `TODO OK` (859 verificaciones). Y a ojo:

```bash
curl -s "$API/api/packages?pageSize=1" | head -c 200
```

**Verificación:** devuelve un paquete con `cancellationPolicy` poblada.

---

## 11. Desplegar el Backoffice

```powershell
.\infra\bootstrap\deploy-backoffice.ps1
```

Compila con `.env.production` (`VITE_API_TARGET=azure`) y publica a `swa-turisclick-v2-backoffice`. El
deployment token se lee de Azure con la sesión de `az` y vive sólo en una variable del proceso.

**Verificación:**

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://ashy-rock-0dd3b480f.2.azurestaticapps.net/
curl -s -o /dev/null -w "%{http_code}\n" https://ashy-rock-0dd3b480f.2.azurestaticapps.net/admin/companies
```

Los dos 200 — el segundo prueba el *fallback* de SPA en una ruta profunda.

Y que el CORS siga alcanzando:

```bash
az webapp config appsettings list -g rg-turisclick-dev -n app-turisclick-v2-api --query "[?starts_with(name,'Cors__')].value" -o tsv
```

**Verificación:** `https://ashy-rock-0dd3b480f.2.azurestaticapps.net` está en la lista.

---

## 12. Smoke tests del Backoffice por rol

Como **ADMIN**:

1. Entrar. **Verificación:** aterriza en *Hoy*, no en una tabla.
2. *Hoy*. **Verificación:** la tarjeta "Empresas esperando aprobación" muestra su estado vacío (no un
   spinner), los contadores traen números reales y "Cobrado y devuelto" muestra importes en formato
   `Bs 1.234,56`.
3. Empresas → *Dar de alta un operador*. **Verificación:** la pantalla de credenciales muestra la contraseña
   temporal una sola vez.
4. Cerrar sesión, entrar con esa cuenta. **Verificación:** va a *Elegí tu contraseña* y no se puede salir de
   ahí.
5. Experiencias, Paquetes, Reservas globales; Reservas con *Necesita atención*; el detalle de pagos de una
   reserva cancelada. **Verificación:** ninguna pantalla queda cargando, no aparece ningún código `UC-*` ni
   un valor de enum crudo, y los importes están formateados.

Como **PROVIDER** (cuenta demo):

6. *Hoy*, Experiencias, Paquetes → *Editar*. **Verificación:** se ven las cinco secciones (datos, itinerario,
   galería, **política de cancelación** con su preview, **vuelo** con aeropuerto, orígenes y clase).
7. Disponibilidad. **Verificación:** el calendario carga y las salidas futuras aparecen.
8. Intentar abrir por URL un paquete de otra empresa. **Verificación:** 403 o 404, nunca el contenido.

---

## 13. Build del APK de preview

```bash
cd frontend/apps/tourist-mobile
npx eas build --platform android --profile preview
```

**Verificación:** el build termina y EAS entrega un `.apk`. El perfil `preview` ya trae
`EXPO_PUBLIC_API_TARGET=azure`, así que apunta al backend desplegado; confirmarlo en *Perfil*, que muestra
contra qué servidor está hablando.

> Esto genera un APK de distribución interna. **No se publica nada en ninguna tienda.**

---

## 14. Smoke tests en Android

Instalar el APK en un teléfono o emulador y recorrer:

1. Registro o ingreso con una cuenta sintética. **Verificación:** entra, y al cerrar y reabrir la app sigue
   la sesión (acá sí funciona el Keystore).
2. Inicio. **Verificación:** el carrusel muestra sólo ciudades con algo que reservar, con su foto.
3. Explorar → filtro de Gastronomía. **Verificación:** 23 experiencias.
4. Detalle de un paquete. **Verificación:** se ve **Si necesitás cancelar** con los tramos de reembolso,
   antes de pagar.
5. Elegir salida, reservar, pagar. **Verificación:** el pago está marcado como simulado y la reserva queda
   confirmada.
6. Mis viajes → la reserva → *Cancelar reserva*. **Verificación:** el presupuesto muestra cuánto se devuelve
   por componente y la frase dice "Cancelás con N días de anticipación", con importes `Bs 1.234,56`.
7. Confirmar la cancelación. **Verificación:** la reserva queda cancelada con su reembolso, y el ADMIN lo ve
   en el libro de pagos con el cobro y el reembolso como dos movimientos distintos.
8. Teclado y textos largos. **Verificación:** en el formulario de pasajeros el teclado no tapa el campo
   activo, y los títulos largos no se desbordan.

> Los puntos 1 y 8 **sólo se pueden verificar acá**: en el build web no existe `expo-secure-store` y no hay
> teclado nativo.

---

## 15. Smoke tests del asistente

1. Pedirle un viaje sin decir cuántas personas. **Verificación:** pide el dato que falta en vez de inventarlo.
2. Completarlo. **Verificación:** propone un itinerario donde **cada actividad apunta a un producto real del
   catálogo**, con precio y cupo revalidados.
3. Guardarlo, salir, volver a abrirlo. **Verificación:** se recupera y se revalida contra el catálogo de ese
   momento.
4. Pedir algo que no existe (un destino sin catálogo). **Verificación:** lo dice; no inventa producto.

Nota: en Azure `Ai__Provider` es `Deterministic`. El asistente funciona y no alucina, pero no usa un modelo de
lenguaje: conviene decirlo en la defensa si surge.

---

## 16. Validación de Duffel TEST

Sólo si se hizo el paso 6. **Una sola** reserva aérea controlada:

1. Elegir un paquete con vuelo y una salida futura.
2. Cotizar desde una ciudad permitida. **Verificación:** vuelven ofertas con precios reales de esa fecha y la
   pantalla muestra el aviso de modo de prueba.
3. Reservar con **datos de pasajero sintéticos**. Nunca un pasaporte o un nombre real.
4. **Verificación:** la orden queda creada en el entorno TEST de Duffel y la reserva queda confirmada con su
   pasaje.
5. Pedir el presupuesto de cancelación. **Verificación:** el reembolso del vuelo es **el que informa la
   aerolínea**, no un porcentaje calculado por nosotros, y el del paquete sale de la política del operador. Si
   la aerolínea no informa, la app no promete un número.
6. Confirmar. **Verificación:** el pasaje queda cancelado del lado de Duffel y los dos reembolsos aparecen
   como movimientos separados en el libro.

---

## 17. Lista de control para la demostración

Antes de empezar, en este orden:

- [ ] Reconstruir la base de demostración si se corrieron escenarios de validación:
      `.\tools\demo\setup-clean-demo.ps1 -Recreate` (local) o el cargador contra Azure.
- [ ] **Despertar la app**: visitar el sitio unos minutos antes. F1 no tiene `Always On` y el primer request
      tarda.
- [ ] `validate-catalog.mjs` da `TODO OK`.
- [ ] Las tres cuentas entran: ADMIN, el operador demo, el viajero demo.
- [ ] Hay al menos una reserva confirmada con política de cancelación, para poder cancelar en vivo.
- [ ] El APK instalado apunta a Azure (verlo en *Perfil*).
- [ ] Tener a mano `POST /api/admin/cancellations/resolve` por si una cancelación queda a medias.
- [ ] Decirlo en voz alta cuando corresponda: **el pago es simulado** y **el vuelo es inventario de prueba de
      Duffel**.

---

## 18. Procedimiento de recuperación

### Si falla a mitad de las migraciones (paso 5)

El script es idempotente y cada migración tiene su transacción: las que entraron, entraron completas.
**Volver a correr el mismo script** — retoma donde quedó.

Si la falla es del esquema y no transitoria: leer el error, corregir la migración **en el repositorio**,
regenerar el script y volver a aplicar. **No** se edita `__EFMigrationsHistory` a mano.

Si hay que volver atrás de verdad: restaurar el volcado del paso 3 sobre una base nueva, verificarla, y
recién entonces decidir. **No** se usan las migraciones `down` como mecanismo de recuperación: borran tablas.

### Si el deploy sale mal (paso 8)

El esquema nuevo es compatible con el código viejo, así que se puede volver al paquete anterior sin tocar la
base:

```bash
az webapp deployment list-publishing-profiles -g rg-turisclick-dev -n app-turisclick-v2-api -o table
```

o relanzar el workflow desde el commit anterior. **Verificación:** los cuatro endpoints del paso 9 vuelven a
200.

### Si los smoke tests fallan tras el deploy (paso 9)

- **500 con `does not exist`** → falta una migración. Volver al paso 4.
- **500 con error de configuración** → una referencia a Key Vault no resuelve. Paso 7.
- **Timeout o 503** → arranque en frío. Reintentar durante un par de minutos antes de concluir nada.
- **Los endpoints públicos andan pero el login da 500** → `users.must_change_password`; falta `0014`.

### Si el Backoffice carga pero no llama a la API (paso 11)

Es CORS. Agregar el origen a `Cors__AllowedOrigins__N` y reiniciar la app.

### Si una cancelación queda colgada

```bash
curl -s -X POST "$API/api/admin/cancellations/resolve" -H "Authorization: Bearer <token de admin>"
```

Devuelve cuántas había, cuántas completó y cuántas quedan. Es idempotente. Si `StillPending` no baja, el
problema no es el proceso de fondo: mirar el detalle de la reserva en el Backoffice.

### Al terminar

Quitar la regla de firewall temporal del paso 2:

```bash
az postgres flexible-server firewall-rule delete -g rg-turisclick-dev -s turisclick-postgres-jpm --rule-name migracion-temporal --yes
```
