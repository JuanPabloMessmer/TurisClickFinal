# Integración Oleada 16 — historial del asistente, explorador de vuelos e imágenes de destinos

Rama: `integration/turisclick-oleada-16`, creada desde `master` (`0e76e23`). **Nada desplegado, nada empujado,
nada aplicado en Azure.** Este documento es el plan para hacerlo.

## Qué entra

| Feature | Origen | Backend | Backoffice | Tourist Mobile | Infra |
|---|---|---|---|---|---|
| Historial del asistente: cada respuesta conserva sus recomendaciones | `0e76e23` (ya en `master`) | sí (`itineraryId` por mensaje) | — | sí | — |
| Explorador de vuelos + `POST /api/flights/search` + FakeFlightProvider ida y vuelta | `b678cda` (`feat/vuelos-explorador-movil`) | sí | — | sí (tab "Vuelos") | — |
| Logo (cumbre y reflejo) | `f316232` (`feat/vuelos-explorador-movil`) | — | sí (sidebar y favicon) | — | — |
| Subida de imagen de destinos | `0eeaca7` (`claude/turisclick-destination-image-upload-259950`) | sí | sí | — (lee `ImageUrl` igual que antes) | Blob Storage |
| `@expo/ngrok` (devDependency) | working tree de `master`, sin commitear | — | — | sólo desarrollo (`--tunnel`) | — |
| CORS del Backoffice en `dev.tfvars` y Duffel opcional en Terraform | esta integración | — | — | — | evita borrar settings |

**Migraciones: ninguna.** No cambió ningún archivo de `Migrations/` y `dotnet ef migrations has-pending-model-changes`
responde "No changes have been made to the model since the last migration". El preflight de esquema del
workflow no debería encontrar migraciones pendientes.

## Qué requiere qué

| Feature | Deploy backend | `terraform apply` | Deploy Backoffice (SWA) | Reiniciar Metro / recargar Expo |
|---|---|---|---|---|
| Historial del asistente | **sí** | no | no | **sí** (sin el backend nuevo, la app cae al comportamiento anterior) |
| Explorador de vuelos | **sí** (sin él, la pestaña muestra error: el endpoint no existe) | no | no | **sí** — reiniciar Metro, es una ruta nueva de expo-router |
| Logo | no | no | **sí** | no |
| Imagen de destinos | **sí** | **sí** (sin la cuenta, la subida responde 503 con mensaje claro) | **sí** | no |
| Duffel TEST | no (configuración) | opcional (`flights_provider = "Duffel"`) | no | no |

## Plan de Terraform (ejecutado `plan`, **no** `apply`)

`terraform plan -var-file=dev.tfvars -lock=false` contra el state real (`stturisclicktfjpm`), 19/10/2026:

```
Plan: 3 to add, 1 to change, 0 to destroy.
```

| Acción | Recurso | Detalle |
|---|---|---|
| create | `azurerm_storage_account.images` (`stturisclickv2imgjpm`) | StorageV2, Standard LRS, Hot, Brazil South, TLS 1.2, sólo HTTPS, sin shared keys, soft delete 7 días, `prevent_destroy` |
| create | `azurerm_storage_container.images` | `images`, lectura anónima de blobs (no de listados) |
| create | `azurerm_role_assignment.app_images_blob_contributor` | Storage Blob Data Contributor, sólo `id-turisclick-v2-app`, sólo sobre el contenedor |
| update in-place | `azurerm_linux_web_app.api` | app settings: se conservan los 8 actuales y se agregan 4 `ImageStorage__*`. Reinicia la app. |

El plan muestra los app settings como "known after apply" porque dos valores dependen de la cuenta nueva. El
mapa final, evaluado con `terraform console`: `ASPNETCORE_ENVIRONMENT`, `Ai__Provider`,
`ConnectionStrings__DefaultConnection` (ref. Key Vault), `Cors__AllowedOrigins__0..2`, `Jwt__Key` (ref. Key
Vault), `Reservations__Expiration__Enabled` y los nuevos `ImageStorage__Provider`, `__AzureBlob__ServiceUri`,
`__AzureBlob__ContainerName`, `__AzureBlob__ManagedIdentityClientId`.

**Hallazgo corregido:** `Cors__AllowedOrigins__2` (el Backoffice publicado) se había cargado a mano y no estaba
en `dev.tfvars`. Terraform reescribe todos los app settings, así que el primer `apply` lo habría borrado y el
Backoffice publicado habría dejado de poder llamar a la API. Ahora está en `dev.tfvars`. Por la misma razón,
Duffel se activa con la variable `flights_provider` y no con `az webapp config appsettings set`.

El state guardará las claves de la cuenta nueva como valores sensibles aunque estén deshabilitadas
(`shared_access_key_enabled = false`); el state ya vive en una cuenta sin shared keys, con acceso por Entra ID.

**Costo estimado** (verificar en la calculadora de Azure): almacenamiento Hot LRS a escala de demo (< 1 GB)
y sus operaciones, centavos de dólar por mes; se espera menos de US$ 1/mes. Sin costo fijo. El plan F1 del App
Service no cambia. Duffel en modo TEST no tiene costo.

## Duffel Sandbox: qué falta en Azure

Hoy la API desplegada usa `FakeFlightProvider` (no hay ningún `Flights__*` en la Web App). En local existe
`Flights:Duffel:AccessToken` en user-secrets; en Key Vault **no** hay token de Duffel. Para activarlo:

1. Cargar el token de prueba en Key Vault sin imprimirlo:
   `az keyvault secret set --vault-name kv-turisclick-v2-dev --name duffel-test-token --value "<TOKEN duffel_test_…>" --output none`
2. `terraform plan -var-file=dev.tfvars -var flights_provider=Duffel`: debe mostrar sólo el update in-place con
   `Flights__Provider` y `Flights__Duffel__AccessToken` (referencia a Key Vault). Recién ahí, `apply`.
3. Verificar en el log de arranque que no aparece el rechazo de token: `DuffelFlightProvider` no arranca con un
   token que no empiece con `duffel_test_` (`RequireTestToken` es `true` por defecto). Ese error es la red de
   seguridad funcionando, no algo para saltear.

La identidad de la app ya tiene `Key Vault Secrets User`; no hace falta otro permiso. El modo Live no se usa.

## Pasos para desplegar (requieren aprobación — ninguno se ejecutó)

0. **Antes de tocar nada:** anotar el deployment activo (`az webapp log deployment list … --query "[?active].id"`;
   el 19/10 era `296c17f3…`) y guardar un respaldo del paquete actual siguiendo
   [wave15-readiness.md §4](wave15-readiness.md#4-rollback).
1. Revisar y fusionar `integration/turisclick-oleada-16` en `master`. En el checkout de `master` hay cambios
   sin commitear en `frontend/package.json` y `package-lock.json` (`@expo/ngrok`): son **idénticos** al commit
   `54a0ae0` de esta rama; descartarlos o commitearlos antes del merge, si no git se niega a fusionar.
2. `git push origin master`. El workflow corre build y tests (no despliega solo).
3. `terraform plan -var-file=dev.tfvars -out=o16.tfplan` y confirmar **3 add / 1 change / 0 destroy** con el
   mapa de settings de arriba. `terraform apply o16.tfplan`. Si una Azure Policy prohíbe
   `allow_nested_items_to_be_public`, el apply falla: decidir excepción o CDN/SAS.
4. Backend: *Run workflow* en "Backend V2 CI/CD" sobre `master` con `deploy = true` (sin `first_deploy_bootstrap`).
   El preflight confirma esquema; los smoke tests y la verificación posterior corren solos.
5. Smoke manual: `GET /api/health` = HEALTHY; subir una imagen desde Swagger o el Backoffice con un token ADMIN
   y abrir la URL devuelta sin sesión; `POST /api/flights/search` con un turista; abrir una conversación del
   asistente y recargarla.
6. Backoffice: `infra/bootstrap/deploy-backoffice.ps1` desde `master` (logo + subida de imágenes).
7. Tourist Mobile: reiniciar Metro (`npx expo start -c`) para tomar la pestaña "Vuelos" y el historial.
8. Duffel TEST, sólo si se decide (sección anterior).

## Rollback

| Qué | Cómo | Riesgo |
|---|---|---|
| Backend | Redesplegar el paquete anterior (respaldo del paso 0) con `az webapp deploy --type zip`, como en wave15-readiness §4. No hay migraciones, así que el código anterior funciona con la base tal cual. | Bajo. Las URLs de imágenes ya guardadas se siguen sirviendo desde Blob aunque la API vuelva atrás. |
| Settings de imágenes | Sin tocar la cuenta: `ImageStorage__Provider` en `None` deja la subida en 503 y todo lo demás igual. Para que no se pierda en el siguiente apply, hacerlo en `locals.tf`. | Bajo. |
| Cuenta de Storage | Tiene `prevent_destroy` y soft delete de 7 días: un `terraform destroy` accidental falla. Borrarla de verdad es una decisión aparte (rompería las URLs ya guardadas). | — |
| Backoffice | Volver a ejecutar `deploy-backoffice.ps1` desde el commit anterior. | Bajo. |
| Tourist Mobile | Volver al commit anterior y reiniciar Metro. La app tolera un backend sin `itineraryId`. | Bajo. |
| Duffel | `flights_provider = "Fake"` y `apply`: vuelve al proveedor falso. | Bajo. |

## V1

No se tocó nada de V1: `turisclick-api` y `ASP-rgturisclickdev-88c3` (East US 2) y la base `turisclick_db`
no aparecen en el plan ni en ningún paso. Toda la inspección de Azure fue de lectura.

## Pruebas de esta integración

| Suite | Resultado |
|---|---|
| Backend (`dotnet test`, base de tests propia `turisclick_v2_test_integ_o16`) | 743/743 |
| Tourist Mobile (jest) | 426/426 (39 suites) |
| Backoffice (vitest) | 229/229 |
| TypeScript (Backoffice, Tourist Mobile) | sin errores |
| oxlint (Backoffice) / ESLint (Tourist Mobile) | 0 errores |
| Build de producción del Backoffice / `expo export` Android + iOS | OK |
| `schema.ts` regenerado desde el Swagger de la API combinada | idéntico al resultado del merge |
| Smoke en vivo (API local, base `turisclick_v2_test_smoke_o16`) | 16/16: subida y servido de imagen, SVG disfrazado rechazado, destino público con la URL, 403 a turista, búsqueda de vuelos ida y vuelta (2 tramos, testMode), 401 anónimo, sin `quoteId`, historial del asistente en orden con `itineraryId` |
