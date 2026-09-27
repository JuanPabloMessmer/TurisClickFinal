# TurisClick — Backoffice

App web de **ADMIN** y **PROVIDER** (React + Vite + React Router + TanStack Query + Tailwind/Radix).
Tourist Mobile (`apps/tourist-mobile`) es otra app: comparten `packages/api-client`, `packages/auth-core` y
`packages/utils`, pero **no** comparten componentes (acá es DOM, allá primitivas de React Native).

## Publicado

<https://ashy-rock-0dd3b480f.2.azurestaticapps.net>

Azure Static Web Apps (plan **Free**, sin costo), recurso `swa-turisclick-v2-backoffice` en
`rg-turisclick-dev`. El build de producción apunta siempre al backend V2 de Azure.

## Alcance

| Rol | Pantallas |
|---|---|
| ADMIN | Destinos (alta/edición, imagen representativa, borrado con reglas), Categorías, Empresas (aprobar, rechazar, suspender, reactivar) y detalle de empresa |
| PROVIDER | Mi Empresa, Experiencias (alta/edición/publicación), Paquetes, calendario de disponibilidad de ambos y Reservas recibidas |

Un PROVIDER solo ve y edita lo suyo: el backend valida ownership en cada request (403) y el guard de rutas
evita navegar a mano a una sección de otro rol. Las pantallas que exigen empresa APPROVED pasan además por
`RequireApprovedCompany`.

## Configuración

Una sola fuente: `src/lib/env.ts`.

| Variable | Backend |
|---|---|
| sin `.env`, o `VITE_API_TARGET=azure` | `https://app-turisclick-v2-api.azurewebsites.net` (por defecto) |
| `VITE_API_TARGET=local` | `http://localhost:5288` |
| `VITE_API_BASE_URL=...` | una URL puntual; pisa a las anteriores |

`.env.production` (versionado) fija `azure`, así que un `.env` local apuntando a `localhost` nunca se cuela
en un build publicado. `VITE_*` queda embebido en el bundle: **no** pongas secretos ahí. La sesión sale del
login real del backend; el refresh token vive en `localStorage` del navegador (ver `lib/tokenStorage.ts`).

## Comandos

```bash
npm run dev        # http://localhost:5173 (origen ya permitido por el CORS de desarrollo)
npm run build      # build de producción (API de Azure)
npm test           # Vitest (guards de rol, errores del backend, calendario)
npm run lint       # oxlint
```

## Deploy

```powershell
.\infra\bootstrap\deploy-backoffice.ps1
```

Compila y publica en Static Web Apps. El deployment token se lee de Azure con la sesión de `az` y viaja por
variable de entorno del proceso: nunca se imprime ni se versiona. El recurso se creó con `az` y **no** está
en el state de Terraform (ver `infra/README.md`).

El dominio publicado tiene que estar en `Cors:AllowedOrigins` del backend (hoy, app setting
`Cors__AllowedOrigins__2` de `app-turisclick-v2-api`).

## Decisiones que conviene conocer

- **El backend es la autoridad.** El cliente no recalcula cupos, precios ni permisos: muestra lo que
  responde la API y traduce sus errores (`lib/errors.ts` entiende `ProblemDetails` y los 400 de validación).
- **Calendario de disponibilidad.** `modules/availability-calendar` es compartido por experiencias y
  paquetes: programador por rango + patrón semanal (todos los días, lunes a viernes, fines de semana,
  personalizado) con vista previa del servidor (`dryRun`) antes de crear, y vista mensual para cambiar cupo
  o abrir/cerrar una fecha. El cupo nunca baja de lo ya reservado y cerrar solo frena reservas nuevas.
- **Imagen de destino.** El `PUT` de destinos reemplaza nombre e imagen juntos, así que el formulario
  precarga la imagen actual: renombrar no la borra.
