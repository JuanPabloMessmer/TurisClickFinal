# Runbook de la demo

Cómo dejar TurisClick listo para mostrarlo, qué mostrar y en qué orden. Está escrito para correr **todo en
local**: a la fecha de esta oleada nada de lo que sigue está desplegado (ver *Qué falta para desplegar*).

El recorrido que propone esta guía dura unos 20 minutos y toca las tres piezas: el panel del administrador, el
panel del operador y la app del viajero.

---

## 1. Qué necesitás

| Pieza | Requisito |
|---|---|
| Backend | .NET 10 SDK, PostgreSQL 16 local |
| Backoffice | Node 20+ |
| Tourist Mobile | Node 20+ y, para verlo en un teléfono, Expo Go (o un emulador) |
| Catálogo demo | Node 20+ y las contraseñas demo en `%USERPROFILE%\.turisclick-secrets` (DPAPI) |

Secretos: la contraseña del administrador sembrado vive en `dotnet user-secrets` (`Seed:AdminPassword`) para
local y en Key Vault para Azure. **Ninguna contraseña se escribe en el repositorio.** El token de Duffel (modo
TEST) también vive sólo en `user-secrets`.

---

## 2. Preparar la base de datos

La base de la demo tiene que estar **limpia**. La base de desarrollo que usás día a día comparte servidor con
las pruebas de integración y los E2E, así que acumula empresas, categorías y experiencias de prueba
(`Interés-Ai-…`, `Tour O8 …`, `Operador E2E …`). Eso no rompe nada, pero se ve en la demo: aparecen en el
listado global del administrador y en el selector de categorías del operador.

Hay un script que hace todo esto de una:

```powershell
.\tools\demo\setup-clean-demo.ps1
```

Crea `turisclick_v2_demo` si no existe, le aplica las migraciones de EF y le carga el catálogo curado. Es
idempotente: volver a correrlo no duplica nada. Para empezar de cero, `-Recreate` (pide tipear el nombre de la
base antes de borrar).

Se niega a escribir en `turisclick_db` (que es V1), en `turisclick_v2_dev`, en `turisclick_v2_test` o en
cualquier nombre que no contenga `demo`, y sólo trabaja contra `localhost`.

Resultado esperado: 8 empresas, 100 experiencias, 13 paquetes —los 13 con política de cancelación—, 43
destinos con foto, 6 categorías sin residuos, 2.000 fechas futuras y 108 salidas.

> El seed de desarrollo (`Seed:Enabled=true`) crea el administrador `admin@turisclick.dev`, los destinos de
> Bolivia y las categorías base. No crea empresas: eso lo hace el catálogo demo.

> **Si corrés los escenarios de validación**, tené en cuenta que escriben en la base: crean operadores,
> experiencias, paquetes y reservas de prueba que después aparecen en el catálogo. Reconstruí con `-Recreate`
> antes de la demostración.

---

## 3. Levantar el backend

```bash
dotnet run --project src/TurisClick.Api --urls http://localhost:5288
```

Comprobación: `curl http://localhost:5288/api/destinations?type=CITY` devuelve 200.

---

## 4. Cargar el catálogo demo

El cargador es **idempotente** y usa sólo los endpoints normales de la API: ningún `INSERT` a mano, ninguna
migración de datos. Da de alta las 8 empresas ficticias (el ADMIN las crea y cada operador cambia su
contraseña temporal, igual que haría una persona), publica 100 experiencias y 13 paquetes, abre el calendario y
aplica la foto de cada destino.

```powershell
$env:TURISCLICK_API = 'http://localhost:5288'
.\tools\demo-catalog\run-catalog.ps1 load-catalog.mjs
```

`run-catalog.ps1` lee la contraseña del administrador de Key Vault. Para correr **sin Azure**, exportá
`ADMIN_PASSWORD` desde `dotnet user-secrets` y las contraseñas demo desde el almacén DPAPI, y llamá a
`node tools/demo-catalog/load-catalog.mjs` directamente.

El calendario es **relativo a hoy**: arranca en 4 días y cubre 6 meses. No hay fechas fijas que se venzan.

Validación (sólo lecturas):

```powershell
.\tools\demo-catalog\run-catalog.ps1 validate-catalog.mjs
```

Contra la base de demostración limpia da **`TODO OK` (859 verificaciones)**. Sobre la base de desarrollo
compartida reporta fallas esperables (más experiencias publicadas que las del catálogo, categorías extra): son
diferencias de la base, no del producto.

---

## 5. Levantar los dos frontends

```bash
npm install --prefix frontend
npm run dev --prefix frontend/apps/backoffice
```

El backoffice queda en `http://localhost:5173`, que ya está permitido por CORS en desarrollo.

```bash
cd frontend/apps/tourist-mobile
npx expo start
```

Con `EXPO_PUBLIC_API_TARGET=local` la app deduce la URL según dónde corra: `localhost` en el simulador de iOS,
`10.0.2.2` en el emulador de Android y la IP de LAN de la PC en un teléfono físico. Perfil muestra contra qué
servidor está hablando, así que si algo no carga, ese es el primer lugar donde mirar.

---

## 6. Cuentas de la demo

| Quién | Email | Contraseña |
|---|---|---|
| Administrador | `admin@turisclick.dev` | `Seed:AdminPassword` de `user-secrets` |
| Operador (Altura Viva) | `proveedor.demo@turisclick.dev` | `demo-provider-password.dpapi` |
| Viajero | `turista.demo@turisclick.dev` | `demo-tourist-password.dpapi` |

Si una contraseña de operador se perdió, el administrador la regenera desde **Empresas → ver detalle →
Accesos → Regenerar contraseña**. El cargador del catálogo también se recupera solo de eso: si la cuenta existe
pero su contraseña no es la demo, regenera la credencial y vuelve a hacer el primer ingreso.

---

## 7. El recorrido

### A. El administrador ve la plataforma (4 min)

1. **Hoy.** Entrá como administrador. La pantalla abre con lo que pide acción (cancelaciones sin resolver,
   pasajes sin confirmar, operadores que todavía no entraron) y sólo si existe. Debajo, los números reales:
   empresas operando, experiencias y paquetes publicados, paquetes con vuelo, reservas de hoy y de la semana,
   y el libro de pagos **por moneda** (no se suman monedas distintas: TurisClick no convierte).
2. **Dar de alta un operador.** Empresas → *Dar de alta un operador*. Cargá los datos y mostrá la pantalla de
   credenciales: la contraseña temporal se muestra **una sola vez**.
3. **El primer ingreso es obligatorio.** Cerrá sesión, entrá con esa cuenta y mostrá que la app te lleva a
   *Elegí tu contraseña* y que no se puede ir a ninguna otra pantalla hasta cambiarla. El bloqueo está en la
   autorización, no en la UI: con ese token, cualquier endpoint de operador responde 403.
4. **Catálogo y reservas globales.** Experiencias y Paquetes de todas las empresas; Reservas con el filtro
   *Necesita atención*. Mostrá el detalle de pagos de una reserva cancelada: cada movimiento es una fila y el
   historial no se reescribe.

### B. El operador arma su producto (6 min)

1. Entrá como operador. **Hoy** muestra las próximas salidas con el cupo vendido y las reservas recibidas.
2. **Paquetes → Editar.** Recorré las cinco secciones: datos generales, itinerario día a día (experiencias
   reales de la empresa + ítems descriptivos), galería, **política de cancelación** (con el preview de lo que
   va a leer el viajero) y **vuelo** (aeropuerto de destino, ciudades de salida, clase).
3. **Disponibilidad.** Mostrá el calendario de salidas.

### C. El viajero reserva (8 min)

1. **Descubrir.** La portada muestra sólo ciudades donde hay algo que reservar hoy.
2. **Gastronomía.** Buscá *Ruta de la salteña paceña* o *Chicherías del valle*: 23 experiencias
   gastronómicas, cada una en la ciudad donde el plato realmente existe.
3. **Detalle de un paquete.** Itinerario, condiciones y **Si necesitás cancelar**, con los tramos de reembolso
   antes de pagar.
4. **Paquete con vuelo.** Elegí salida y ciudad de origen: el pasaje se cotiza en el momento contra Duffel en
   modo TEST, con precios reales de esa fecha.
5. **Pagar.** El pago es simulado y está marcado como tal. Mostrá el comprobante.
6. **Cancelar.** Pedí el presupuesto de cancelación: el servidor calcula cuánto se devuelve de **cada
   componente** con su propia política (la del paquete no se aplica al pasaje, y lo que la aerolínea no informa
   no se promete). Confirmá y mostrá los reembolsos en el libro de pagos del administrador.
7. **Asistente.** Contale un viaje. Arma el itinerario con experiencias reales, fechas y precios del catálogo,
   y no inventa producto: si no hay nada que encaje, lo dice.

---

## 8. Si algo falla en vivo

| Síntoma | Causa más probable |
|---|---|
| La app móvil no carga nada | Apunta a otro backend. Mirá el servidor en Perfil. |
| El backoffice no llama a la API | El origen no está en `Cors:AllowedOrigins` de `appsettings.Development.json`. |
| `no se puede crear disponibilidad para una fecha pasada` | El reloj de la PC está atrasado respecto a las fechas cargadas. |
| El operador recibe 403 en todo | No cambió su contraseña temporal. |
| Un paquete no se puede cancelar desde la app | No tiene política de cancelación: es correcto, y el detalle lo dice. |
| El vuelo no cotiza | Falta `Flights:Duffel:AccessToken` en `user-secrets`, o Duffel TEST no responde. |
| Una cancelación queda "esperando resolución" | `POST /api/admin/cancellations/resolve` la reintenta ahora. Es idempotente. |
| El login da 500 en Azure | Falta la migración `0014` en `turisclick_db_v2`. Ver el plan de despliegue. |

---

## 9. Qué falta para desplegar

> El procedimiento completo, paso por paso y con su verificación, está en
> [azure-v2-deployment-plan.md](azure-v2-deployment-plan.md). El estado actual de cada pieza está en
> [deployment-readiness.md](deployment-readiness.md).
>
> **Al 8 de octubre de 2026 el backend V2 en Azure está caído**: el código de las Oleadas 12 y 13 se desplegó
> automáticamente sin aplicar las migraciones, así que `/api/auth/login` y `/api/packages` responden 500.
> Arreglarlo es el paso 5 de ese plan.

Resumen de lo que falta:

Nada de esta oleada está en Azure, y desplegarlo **no** es sólo correr el pipeline. Lo que falta:

1. **Migraciones.** `0012`–`0014` están aplicadas sólo en local. En Azure hay que aplicarlas contra
   `turisclick_v2_dev` (no contra `turisclick_db`, que es V1 y no se toca). Las tres son aditivas: tablas
   nuevas, columnas nullable, índices y checks. Ninguna borra ni reescribe datos.
2. **El secreto de Duffel.** `Flights:Duffel:AccessToken` no está en Azure. Tiene que entrar por Key Vault y
   leerse como referencia desde App Service, nunca como texto en `appsettings` ni en una variable del pipeline.
   Sigue siendo el token de **modo TEST**.
3. **CORS.** El origen del backoffice desplegado tiene que estar en `Cors:AllowedOrigins` del entorno.
4. **El servicio de resolución de cancelaciones.** `CancellationResolutionBackgroundService` corre dentro del
   proceso de la API. En App Service con el plan gratuito la app se duerme sin tráfico, así que una
   cancelación que quedó pendiente puede tardar en resolverse. Hay que decidir entre mantener la app siempre
   encendida (`Always On`, que requiere plan pago) o mover la resolución a un trabajo agendado.
5. **El catálogo demo.** Hay que correr el cargador contra el backend desplegado (`TURISCLICK_API`), porque las
   fotos de destino y el calendario viven en la base, no en el código.
6. **La app móvil.** Confirmar `EXPO_PUBLIC_API_TARGET=azure` y generar el build; el bundle lleva la URL
   embebida.
7. **Qué NO cambia:** Terraform, V1, la configuración del servidor PostgreSQL de Azure y el modo TEST de
   Duffel.
