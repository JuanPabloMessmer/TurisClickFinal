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

# Sólo orígenes de desarrollo hasta desplegar el frontend.
cors_allowed_origins = [
  "http://localhost:5173",
  "http://localhost:8081",
]
