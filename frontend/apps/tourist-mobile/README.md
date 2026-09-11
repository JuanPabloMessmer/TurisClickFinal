# TurisClick — Tourist Mobile

App del **turista** (React Native + Expo Router). Es la cara pública del marketplace: el catálogo entero
se navega sin cuenta, y la sesión solo hace falta para lo privado.

El Backoffice (`apps/backoffice`) es otra app, para PROVIDER y ADMIN. Comparten `packages/api-client`,
`packages/auth-core` y `packages/utils`; **no** comparten componentes: acá todo son primitivas de React
Native y allá es DOM. Lo único visual en común es la paleta.

## Alcance de Fase 1

Implementado:

| Pantalla | Sesión | Casos de uso |
|---|---|---|
| Inicio | pública | UC-T-03 (destinos), UC-T-04, UC-T-06 |
| Explorar (filtros + scroll infinito) | pública | UC-T-04, UC-T-06 |
| Detalle de experiencia | pública | UC-T-05 |
| Detalle de paquete | pública | UC-T-07 |
| Login / Crear cuenta | — | UC-AUTH-01, UC-AUTH-02 |
| Perfil | privada (con CTA si no hay sesión) | — |

**Fuera de Fase 1:** reservar, pagar, cancelar, "Mis viajes", chat y itinerarios con IA, notificaciones,
mapas y development build. El detalle muestra el CTA de reserva **deshabilitado y rotulado "Reservas
disponibles próximamente"**: prepara el layout definitivo sin simular una función que todavía no existe.
Hay un test que lo fija (`src/features/catalog/__tests__/detail.test.tsx`).

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
  _layout.tsx            providers; SIN guard global — el catálogo es público
  (tabs)/                Inicio, Explorar, Perfil
  (auth)/                login y registro, presentados como modal
  experience/[id].tsx
  package/[id].tsx
src/
  auth/session.tsx       SessionProvider + gating por rol
  features/catalog/      hooks de datos, tarjetas y piezas de los detalles
  lib/                   env, httpClient, secure storage, mapeo de errores, queryClient
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
