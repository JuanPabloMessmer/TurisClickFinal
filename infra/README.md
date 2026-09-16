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
└── kv-turisclick-v2-dev ........... Terraform (RBAC, prevent_destroy, sin secretos)

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
| 8 | `bootstrap/run-migrations.ps1` | pendiente |
| 9 | `bootstrap/set-keyvault-secrets.ps1 -Secret seed-admin-password` y `bootstrap/run-seed.ps1` (ejecución única) | pendiente |
| 10 | `bootstrap/lock-postgres.ps1` (después de 6, 8 y 9) | pendiente |
| 11 | Primer deploy del backend y smoke tests | pendiente |
| 12 | GitHub Actions con OIDC | pendiente |

Los pasos 6, 8 y 9 abren una regla de firewall temporal sólo para la IP actual y la eliminan al terminar. Por eso el lock del servidor va después: bloquearía el borrado de esa regla.

## Plan de servicio: F1

Gratis, pero con límites a tener en cuenta para una demo: 60 minutos de CPU por día, sin Always On (la app se duerme y el primer request tarda), 1 GB de RAM y de disco. La expiración automática de reservas no corre mientras la app está dormida. Para la defensa se puede pasar a `B1` cambiando `app_service_sku` en `dev.tfvars`.
