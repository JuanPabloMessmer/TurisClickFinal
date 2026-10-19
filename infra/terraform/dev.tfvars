# Ambiente dev/demo de TurisClick V2. Sin secretos: este archivo está versionado.

environment          = "dev"
location             = "brazilsouth"
resource_group_name  = "rg-turisclick-dev"
postgres_server_name = "turisclick-postgres-jpm"
database_name        = "turisclick_db_v2"
app_service_sku      = "F1"
web_app_name         = "app-turisclick-v2-api"
key_vault_name       = "kv-turisclick-v2-dev"
ai_provider          = "Deterministic"

# Orígenes de desarrollo + el Backoffice publicado en Static Web Apps. El tercero se cargó a mano en la Web
# App (ver infra/README.md); tiene que estar acá porque Terraform reescribe todos los app settings y, sin él,
# el próximo apply lo borraría y el Backoffice publicado dejaría de poder llamar a la API (CORS).
cors_allowed_origins = [
  "http://localhost:5173",
  "http://localhost:8081",
  "https://ashy-rock-0dd3b480f.2.azurestaticapps.net",
]
