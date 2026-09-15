# TurisClick — Tourist Mobile

App del **turista** (React Native + Expo Router). Es la cara pública del marketplace: el catálogo entero
se navega sin cuenta, y la sesión solo hace falta para lo privado.

El Backoffice (`apps/backoffice`) es otra app, para PROVIDER y ADMIN. Comparten `packages/api-client`,
`packages/auth-core` y `packages/utils`; **no** comparten componentes: acá todo son primitivas de React
Native y allá es DOM. Lo único visual en común es la paleta.

## Alcance (Fase 1 + Fase 2)

| Pantalla | Sesión | Casos de uso |
|---|---|---|
| Inicio | pública | UC-T-03 (destinos), UC-T-04, UC-T-06 |
| Explorar (filtros + scroll infinito) | pública | UC-T-04, UC-T-06 |
| Detalle de experiencia / paquete | pública | UC-T-05, UC-T-07 |
| Elegir fecha o salida + viajeros (`book/…`) | pública para elegir; crear exige sesión | UC-T-08, UC-T-09 |
| Checkout (`checkout/[id]`) | TOURIST | UC-T-19, UC-SYS-02, UC-T-11 |
| Mis viajes (tab `trips`) | contenido TOURIST; la tab es pública | UC-T-10 |
| Detalle de reserva (`reservation/[id]`) | TOURIST | UC-T-10, UC-T-11 |
| Login / Crear cuenta | — | UC-AUTH-01, UC-AUTH-02 |
| Perfil | con CTA si no hay sesión | — |

**Fuera de alcance:** chat e itinerarios con IA, mapas, notificaciones, reseñas, favoritos, pasarela de
pago real, reembolsos, app de proveedor/admin, subida de imágenes y development build.

Inicio solo muestra lo que los datos sostienen: destinos, experiencias nuevas y paquetes nuevos (ambas
búsquedas ordenan por fecha de creación descendente). No hay "populares", "trending" ni "recomendados"
porque no existe ninguna métrica detrás. Los destinos se ordenan por `publishedExperienceCount`, que es
un número que calcula el backend —no una popularidad inventada— y por eso La Paz aparece primero en vez
de Bermejo.

Los filtros de Explorar son exactamente los parámetros que aceptan los endpoints: destino, categoría,
precio máximo, disponibilidad desde hoy y, solo en paquetes, duración en días. No hay búsqueda por texto
libre ni ordenamientos, porque el backend no los expone.

## Requisitos

- Node 20+
- El backend corriendo (por defecto en `http://localhost:5288`)
- Expo Go en el teléfono, o un emulador de Android / simulador de iOS

## Puesta en marcha

```bash
npm install            # desde frontend/ (workspaces)
cd apps/tourist-mobile
npx expo start
```

### Variable de entorno

`EXPO_PUBLIC_API_BASE_URL` apunta al backend. Copiá `.env.example` a `.env` y elegí la URL según **dónde
corre la app**, no dónde corre el backend — `localhost` significa cosas distintas en cada caso:

| Dónde corre la app | URL |
|---|---|
| iOS Simulator | `http://localhost:5288` |
| Android Emulator | `http://10.0.2.2:5288` |
| Teléfono físico (Expo Go) | `http://<IP-LAN-de-tu-PC>:5288` |

Si no la definís, `src/lib/env.ts` deduce una: en teléfono físico toma la IP de la máquina que sirve
Metro, que es justamente la que el teléfono ya puede alcanzar. Para que el teléfono llegue al backend,
este tiene que escuchar en la LAN y no solo en loopback:

```bash
dotnet run --project src/TurisClick.Api --urls http://0.0.0.0:5288
```

`EXPO_PUBLIC_*` queda embebido en el bundle: no pongas secretos ahí.

## Comandos

```bash
npx expo start        # servidor de desarrollo
npm test              # Jest
npm run typecheck     # tsc --noEmit
```

## Estructura

```
app/                     rutas (Expo Router, file-based)
  _layout.tsx            providers + SessionQuerySync; SIN guard global — el catálogo es público
  (tabs)/                Inicio, Explorar, Mis viajes, Perfil
  (auth)/                login y registro, presentados como modal
  experience/[id].tsx    detalle público
  package/[id].tsx       detalle público
  book/experience/[id]   elegir fecha + viajeros
  book/package/[id]      elegir salida + viajeros
  checkout/[id].tsx      pagar (TOURIST)
  reservation/[id].tsx   detalle de reserva (TOURIST)
src/
  auth/session.tsx       SessionProvider + gating por rol
  auth/RequireTourist    guard POR PANTALLA (no global)
  auth/SessionQuerySync  borra la caché privada del turista que se va
  features/catalog/      hooks de datos, tarjetas y piezas de los detalles
  features/booking/      selección de fecha/viajeros y precio estimado
  features/reservations/ keys, queries/mutations, modelo de estados, checkout, Mis viajes
  lib/                   env, httpClient, secure storage, mapeo de errores, queryClient
  test-utils/            fábricas de datos para tests
  ui/                    design system (Screen, Button, TextField, Price, Chip, estados…)
  theme/colors.ts        paleta
```

Las piezas compartidas por los dos detalles viven en `src/features/catalog/detail.tsx` y no dentro de un
archivo de ruta, porque Expo Router solo consume el `export default` de cada pantalla.

## Decisiones que conviene conocer antes de tocar el código

**El catálogo es público y no hay guard global.** Cualquiera puede navegar Inicio, Explorar y los
detalles sin cuenta; Perfil, sin sesión, muestra un CTA en vez de bloquear. Si en Fase 2 agregás una
pantalla privada, pedí la sesión en esa pantalla — no reintroduzcas un guard en la raíz.

**El gating por rol vive acá, no en `auth-core`.** El backend emite sesión para cualquier rol válido
(verificado: un login de PROVIDER devuelve 200 con tokens). Esta app rechaza todo lo que no sea
`TOURIST`, en login **y** al restaurar sesión al abrir, y en ambos casos hace `logout()` para no dejar
el refresh token guardado en el dispositivo. `auth-core` se mantiene agnóstico porque el Backoffice
necesita exactamente los roles contrarios.

**Los tipos generados marcan todo opcional.** ASP.NET no emite `required` en el OpenAPI, así que en
`api-client` casi todo es `T | undefined`. La respuesta no es `!` por todos lados: los componentes
aceptan valores ausentes y dicen la verdad cuando faltan (`Price` muestra "Consultar precio" en vez de
un `0.00` inventado), y los filtros descartan opciones sin `id`.

**El summary trae `coverImageUrl`; el detalle trae `images[]`.** `coverImageUrl()` en
`src/features/catalog/images.ts` resuelve la portada y tolera una cover sin `url`.

## Notas de monorepo (por qué `metro.config.js` y `jest.config.js` tienen configuración extra)

Los `@turisclick/*` se consumen como **TypeScript fuente**, sin build previo. Eso obliga a tres cosas:

1. **Metro** necesita `watchFolders` + `nodeModulesPaths` apuntando a la raíz. Lo que **no** hay que
   hacer es activar `disableHierarchicalLookup`: rompe las dependencias transitivas anidadas
   (`react-native-reanimated` trae su propio `semver@7`, y el hoisteado en la raíz es `semver@6`).
2. **Jest** tiene que transpilar esos paquetes, así que `@turisclick` está en la lista de excepciones de
   `transformIgnorePatterns`. Cuidado con los prefijos: no llevan separador final, porque `expo` también
   tiene que cubrir a `expo-modules-core`.
3. **React está duplicado en el monorepo**: esta app usa el que fija Expo SDK 57 (anidado) y el
   Backoffice hoistea otro. `@testing-library/react-native` vive hoisteado y resolvería el segundo, con
   lo que un test correría con dos Reacts a la vez; el `moduleNameMapper` de `jest.config.js` fuerza a
   todos a la copia de esta app.

## Validar en el navegador (opcional)

Para mirar las pantallas sin un teléfono se puede correr la app en web:

```bash
npx expo start --web
```

El backend de desarrollo tiene que aceptar ese origen: `appsettings.Development.json` ya incluye
`http://localhost:8081` en `Cors:AllowedOrigins`. Dos límites a tener en cuenta antes de sacar
conclusiones de lo que se ve ahí:

- `expo-secure-store` no existe en web, así que la sesión no sobrevive a un refresh. Dentro de una misma
  carga de la app funciona igual que en el teléfono.
- TanStack Query pausa sus reintentos cuando el documento no está visible. Con la pestaña en segundo
  plano una request fallida se queda esperando y la pantalla muestra skeletons; con la pestaña al frente
  falla y aparece el estado de error. No es un problema de la app.

Expo Go sigue siendo el entorno de referencia de Fase 1.

## Fase 2: reservas, pago y Mis viajes

**Flujo.** Detalle → "Elegir fecha/salida" → `book/…` (fecha, viajeros, estimado) → "Continuar" → si no hay
sesión, login en modal y vuelta a la misma pantalla con la selección intacta → "Continuar" de nuevo →
`POST /api/reservations` → `router.replace` al checkout → pagar → confirmada → Mis viajes.

**Decisiones que conviene conocer:**

- **El backend es la autoridad.** El frontend no revalida precio ni cupo ni decide expiraciones: interpreta
  lo que devuelve la API (`src/features/reservations/model.ts`).
- **Crear reserva no es idempotente en el backend.** Por eso la mutation tiene `retry: false`, el botón se
  bloquea de forma síncrona (ref) además de `isPending`, la reserva **nunca** se crea sola después del login,
  y ante un corte de red se pide revisar Mis viajes antes de reintentar.
- **Cambio de precio al pagar** es un paso del flujo, no un error: se muestran snapshot vs. precio vigente y
  los nuevos totales por moneda (`currentUnitPrice × travelers`); "Aceptar y pagar" reenvía con
  `acceptPriceChanges: true`. El importe final es el que confirma la respuesta.
- **Multimoneda:** una fila por moneda (`totals` del backend). Nunca se suman monedas distintas.
- **Expiración:** cuenta regresiva local desde `expiresAt`. En 00:00 se deshabilita "Pagar" y se re-lee la
  reserva; si el backend todavía devuelve `PENDING_PAYMENT`, se re-lee cada 30 s como máximo 4 veces. El
  estado nunca lo cambia el frontend.
- **Pago simulado:** "Pagar" envía `success: true`. "Simular rechazo" existe **solo en desarrollo**
  (`IS_DEVELOPMENT`, `__DEV__`).
- **Caché privada:** las keys de reservas llevan el id del usuario (`['reservations', userId, …]`), y
  `SessionQuerySync` borra el scope del turista que se va (logout, refresh fallido, cambio de cuenta). El
  catálogo público no se toca.
- **Mis viajes:** una sola lista paginada en el orden del backend. No hay pestañas por estado porque
  `/api/reservations/me` no filtra, y filtrar solo lo cargado mostraría resultados incompletos.
- **Errores:** los 5xx nunca muestran `ProblemDetails.detail` (el backend expone ahí el mensaje crudo de la
  excepción). Los 400 de validación muestran solo mensajes aptos para personas.

**Limitaciones conocidas (backend, sin corregir en esta fase):** sin idempotencia al crear reservas; "hoy"
se calcula en UTC (en Bolivia, desde las 20:00 locales los slots del día desaparecen); dos formas de 410 al
pagar; `PAYMENT_FAILED` nunca se escribe; `totals` incluye líneas canceladas. Detalle en
`docs/backend-architecture.md` → "Deuda técnica conocida".
