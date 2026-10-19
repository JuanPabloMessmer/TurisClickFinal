# Infraestructura Azure — TurisClick V2

Un solo ambiente dev/demo, en Brazil South, junto a la V1 que sigue desplegada.

```
rg-turisclick-dev (compartido con V1)
├── V1 — fuera de Terraform, no se toca
│   ├── ASP-rgturisclickdev-88c3 / turisclick-api (eastus2)
│   └── oidc-msi-9aac
├── turisclick-postgres-jpm ........ data source (existente, compartido)
│   ├── turisclick_db .............. V1 — no aparece en Terraform
│   └── turisclick_db_v2 ........... Terraform, prevent_destroy
├── asp-turisclick-v2-dev .......... Terraform (F1 Linux)
├── app-turisclick-v2-api .......... Terraform (.NET 10, HTTPS)
├── id-turisclick-v2-app ........... Terraform (user-assigned)
├── kv-turisclick-v2-dev ........... Terraform (RBAC, prevent_destroy, sin secretos)
├── stturisclickv2imgjpm / images .. Terraform (imágenes subidas) — definido, PENDIENTE de apply
└── swa-turisclick-v2-backoffice ... az CLI, FUERA del state (Static Web Apps, Free, East US 2)

rg-turisclick-tfstate
└── stturisclicktfjpm / tfstate .... bootstrap (Entra ID, sin shared keys, lock)
```

## Qué NO gestiona Terraform, y por qué

| Elemento | Dónde se gestiona | Motivo |
|---|---|---|
| Resource group, servidor PostgreSQL | data source | Compartidos con V1: fuera del state, ningún plan puede modificarlos ni destruirlos |
| `turisclick_db` (V1) | nadie | Sus migraciones son incompatibles con V2 |
| Backend del state | `bootstrap/bootstrap-state.ps1` | No puede vivir en el state que protege |
| Rol `turisclick_v2_app` de PostgreSQL | `bootstrap/create-db-role.ps1` | Con Terraform, las credenciales quedarían en la configuración o en el state |
| Valores de los secretos | `bootstrap/*.ps1` | Terraform sólo conoce los nombres; los valores nunca entran al state |
| Lock del servidor PostgreSQL | `bootstrap/lock-postgres.ps1` | La identidad de Terraform no necesita permisos sobre locks |
| Registro de resource providers | `bootstrap/register-providers.ps1` | azurerm 5.x no los registra solo; se hace una vez, a mano |
| `swa-turisclick-v2-backoffice` (Backoffice) | `az staticwebapp create` + `bootstrap/deploy-backoffice.ps1` | Creado a mano para publicar el Backoffice; queda pendiente decidir si se incorpora al state (ver abajo) |

## Secretos

- Ninguno en `.tf`, `.tfvars`, outputs, state ni Git.
- La app los recibe como referencias `@Microsoft.KeyVault(VaultName=...;SecretName=...)`, resueltas por App Service con la identidad `id-turisclick-v2-app` (rol `Key Vault Secrets User`).
- Los scripts generan los valores en memoria y los cargan por REST: nunca se imprimen, ni pasan por argumentos, ni se escriben a disco.

| Secreto | Lo carga | Usado por |
|---|---|---|
| `db-connection-string` | `create-db-role.ps1` | `ConnectionStrings__DefaultConnection` |
| `jwt-key` | `set-keyvault-secrets.ps1 -Secret jwt-key` | `Jwt__Key` |
| `seed-admin-password` | `set-keyvault-secrets.ps1 -Secret seed-admin-password` | `run-seed.ps1` (no la app) |

## Ejecutar Terraform

Desde `infra/terraform`, con una sesión `az login`:

```powershell
$env:ARM_SUBSCRIPTION_ID = az account show --query id -o tsv
terraform init
terraform plan -var-file=dev.tfvars -out=turisclick-v2-dev.tfplan
```

Antes de cualquier `apply`, el plan debe mostrar sólo creaciones: ningún update, destroy ni replacement, y ningún recurso de V1.

## Orden de implementación

| # | Paso | Estado |
|---|---|---|
| 1 | `bootstrap/register-providers.ps1` | ✅ ejecutado |
| 2 | `bootstrap/bootstrap-state.ps1` | ✅ ejecutado |
| 3 | `terraform init` / `validate` / `plan` | ✅ ejecutado (sin apply) |
| 4 | `terraform apply` del plan revisado | ✅ ejecutado |
| 5 | `bootstrap/set-keyvault-secrets.ps1 -Secret jwt-key` | ✅ ejecutado |
| 6 | `bootstrap/create-db-role.ps1` | ✅ ejecutado |
| 7 | Reiniciar la Web App y verificar las referencias a Key Vault | ✅ ambas `Resolved` |
| 8 | Migraciones EF Core V2 (mecanismo de `bootstrap/run-migrations.ps1`) | ✅ 8/8 |
| 9 | `bootstrap/set-keyvault-secrets.ps1 -Secret seed-admin-password` y `bootstrap/run-seed.ps1` (ejecución única) | ✅ ejecutado |
| 10 | `bootstrap/lock-postgres.ps1` (después de 6, 8 y 9) | pendiente |
| 11 | Primer deploy manual del backend y smoke tests | ✅ ejecutado |
| 12 | `bootstrap/setup-github-oidc.ps1` y `.github/workflows/backend-v2.yml` | ✅ ejecutado |

Los pasos 6, 8 y 9 abren una regla de firewall temporal sólo para la IP actual y la eliminan al terminar. Por eso el lock del servidor va después: bloquearía el borrado de esa regla.

## CI/CD (GitHub Actions)

```
push a master (src/, tests/, TurisClick.slnx, el workflow) · workflow_dispatch
  └─ build-test (ubuntu, PostgreSQL 16 efímero como service container)
       restore → build Release → tests → publish → artifact
  └─ deploy (sólo master, nunca en pull requests)
       azure/login por OIDC → az webapp deploy (zip) → smoke tests públicos
```

- **Autenticación:** `id-turisclick-v2-github-deploy` (user-assigned) con un federated credential cuyo subject es `repo:JuanPabloMessmer/TurisClickFinal:ref:refs/heads/master`. No hay client secret, publish profile ni secretos de Azure en GitHub; la autenticación básica de la web app sigue deshabilitada.
- **Permisos:** sólo `Website Contributor` sobre `app-turisclick-v2-api`. Nada sobre PostgreSQL, Key Vault, V1 ni la subscription. La identidad de runtime (`id-turisclick-v2-app`) es otra y es la única que lee Key Vault.
- **Tests en CI:** usan un PostgreSQL efímero del job vía `ConnectionStrings__TestDatabase`; su contraseña es un valor fijo de CI, no una credencial.
- **Pull requests:** corren build y tests, sin deploy.
- **Fuera del workflow:** migraciones y seed (`bootstrap/`), infraestructura (Terraform).
- La identidad de deploy se creó con script, fuera de Terraform (el state sigue con los 6 recursos de V2).

## Imágenes subidas (Blob Storage) — pendiente de apply

El Backoffice sube la imagen de cada destino a `POST /api/admin/destinations/images` (sólo ADMIN) y la API
la guarda en Blob Storage; en la base sólo queda la URL HTTPS pública, en el mismo `ImageUrl` de siempre, así
que la app móvil no cambia. Nunca se guarda en el App Service: el directorio de despliegue se reemplaza en
cada deploy.

**Estado:** los recursos están definidos en `terraform/storage.tf` pero **no se aplicaron**. Hasta que exista
la cuenta, la API desplegada responde 503 ("No se pudo guardar la imagen…") a la subida y todo lo demás
—incluidas las URLs externas ya guardadas— sigue funcionando igual.

| Recurso | Configuración |
|---|---|
| `stturisclickv2imgjpm` | StorageV2, Standard **LRS**, Hot, Brazil South, TLS 1.2, sólo HTTPS, **shared keys deshabilitadas**, soft delete 7 días, `prevent_destroy` |
| contenedor `images` | Lectura anónima **de blobs** (cada foto por su URL), no de listados |
| `Storage Blob Data Contributor` | Sólo para `id-turisclick-v2-app`, sólo sobre el contenedor `images` |
| App settings nuevos | `ImageStorage__Provider=AzureBlob`, `ImageStorage__AzureBlob__ServiceUri`, `__ContainerName`, `__ManagedIdentityClientId` |

Sin claves ni SAS en ningún lado: la API escribe con la identidad administrada y el frontend nunca habla con
Storage directamente. Los nombres de blob los genera el servidor (`destinations/AAAA/MM/<guid>.<ext>`) y se
sirven con `Cache-Control: immutable`.

**Costo estimado (verificar en la calculadora de Azure antes de aplicar):** a escala de demo (cientos de
imágenes de ≤ 5 MB, es decir, menos de 1 GB) el almacenamiento Hot LRS cuesta centavos de dólar por mes; las
operaciones de escritura/lectura y la salida de datos a ese volumen también son de centavos. Se espera menos
de US$ 1/mes. No hay costo fijo por la cuenta. Un CDN delante (`ImageStorage__AzureBlob__PublicBaseUrl`) es
opcional y no hace falta para la demo.

**Para activarlo** (desde `infra/terraform`, con `az login`):

1. `terraform plan -var-file=dev.tfvars -out=images.tfplan`. Excepción a la regla de "sólo creaciones": el
   plan debe mostrar **3 creaciones** (cuenta, contenedor, role assignment) y **1 update in-place** de
   `azurerm_linux_web_app.api` (sólo los 4 app settings `ImageStorage__*`). Nada más. El diff de settings
   aparece como "known after apply"; el mapa final se ve con `echo 'local.app_settings' | terraform console
   -var-file=dev.tfvars` y tiene que conservar los 8 settings actuales, incluido `Cors__AllowedOrigins__2`.
2. Si alguna Azure Policy de la subscription prohíbe `allow_nested_items_to_be_public`, el apply falla: en ese
   caso hay que decidir entre una excepción a la política o servir las imágenes por CDN/SAS.
3. `terraform apply images.tfplan`. El cambio de app settings reinicia la Web App.
4. Smoke test: subir una imagen desde el Backoffice y abrir la URL devuelta en un navegador sin sesión.

**Desarrollo local:** `appsettings.Development.json` usa `ImageStorage:Provider=Local`, que guarda en
`%LOCALAPPDATA%/TurisClick/uploads` y la API lo sirve en `/uploads`. Fuera de Development la API se niega a
arrancar con `Local`.

**Limitaciones conocidas:** reemplazar o quitar la imagen de un destino no borra el blob anterior (quedan
huérfanos, de costo despreciable); la subida tampoco redimensiona ni recomprime.

## Plan de servicio: F1

Gratis, pero con límites a tener en cuenta para una demo: 60 minutos de CPU por día, sin Always On (la app se duerme y el primer request tarda), 1 GB de RAM y de disco. La expiración automática de reservas no corre mientras la app está dormida. Para la defensa se puede pasar a `B1` cambiando `app_service_sku` en `dev.tfvars`.

## Static Web App del Backoffice (fuera del state)

| Dato | Valor |
|---|---|
| Recurso | `swa-turisclick-v2-backoffice` |
| Resource group | `rg-turisclick-dev` |
| Región | East US 2 (Static Web Apps no está disponible en Brazil South) |
| SKU | **Free** — sin costo: 100 GB de tráfico y certificado TLS incluidos |
| URL | <https://ashy-rock-0dd3b480f.2.azurestaticapps.net> |
| Creado con | `az staticwebapp create ... --sku Free` (no hay `terraform apply` de por medio) |
| Deploy | `bootstrap/deploy-backoffice.ps1` (build + SWA CLI con el deployment token leído de Azure) |
| Tags | `project=turisclick-v2`, `component=backoffice`, `managed-by=manual-az-cli` |

**Por qué Static Web Apps y no otra cosa:** el Backoffice es un SPA estático (Vite), no necesita servidor.
El plan Free ya trae HTTPS, CDN y fallback de rutas para SPA, y no agrega carga al App Service F1, que ya
está al límite con la API. Servir el SPA desde el propio App Service habría gastado CPU/RAM del plan gratuito
compartido con el backend, y una cuenta de Storage con static website sí tiene costo por almacenamiento y
transferencia. No se creó ningún recurso pago.

**Para incorporarlo a Terraform más adelante:** `azurerm_static_web_app` con `sku_tier = "Free"` en
`rg-turisclick-dev` y luego `terraform import`. No se hizo ahora para no tocar el state en esta oleada.

**CORS:** el dominio publicado está en el app setting `Cors__AllowedOrigins__2` de `app-turisclick-v2-api`
(los otros dos son los orígenes de desarrollo). Se cargó a mano, pero desde la Oleada 16 también está en
`cors_allowed_origins` de `dev.tfvars`: Terraform reescribe todos los app settings, y sin esa línea el primer
`apply` lo habría borrado y el Backoffice publicado habría perdido acceso a la API. Cualquier app setting nuevo
(por ejemplo Duffel, con la variable `flights_provider`) va por Terraform, nunca a mano.
