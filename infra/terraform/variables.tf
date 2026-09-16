variable "environment" {
  description = "Nombre corto del ambiente. Por ahora sólo existe dev/demo."
  type        = string
  default     = "dev"
}

variable "location" {
  description = "Región de los recursos V2. Misma región que el PostgreSQL existente."
  type        = string
  default     = "brazilsouth"
}

variable "resource_group_name" {
  description = "Resource group existente, compartido con V1. Se lee como data source."
  type        = string
  default     = "rg-turisclick-dev"
}

variable "postgres_server_name" {
  description = "PostgreSQL Flexible Server existente. Se lee como data source: Terraform nunca lo gestiona."
  type        = string
  default     = "turisclick-postgres-jpm"
}

variable "database_name" {
  description = "Base de datos V2, nueva y separada de V1."
  type        = string
  default     = "turisclick_db_v2"

  validation {
    # turisclick_db es V1 y sus migraciones son incompatibles con V2.
    condition     = var.database_name != "turisclick_db"
    error_message = "turisclick_db pertenece a V1 y no puede ser gestionada por esta configuración."
  }
}

variable "app_service_sku" {
  description = "SKU del plan. F1 para desarrollo; B1 si hace falta Always On (por ejemplo, para la defensa)."
  type        = string
  default     = "F1"

  validation {
    condition     = contains(["F1", "B1"], var.app_service_sku)
    error_message = "Sólo se contemplan F1 y B1 para este ambiente."
  }
}

variable "web_app_name" {
  description = "Nombre global de la Linux Web App V2."
  type        = string
  default     = "app-turisclick-v2-api"
}

variable "key_vault_name" {
  description = "Nombre global del Key Vault V2 (3-24 caracteres)."
  type        = string
  default     = "kv-turisclick-v2-dev"
}

variable "cors_allowed_origins" {
  description = "Orígenes CORS de la API. Inicialmente sólo los de desarrollo; se actualiza al desplegar el frontend."
  type        = list(string)
  default     = []
}

variable "ai_provider" {
  description = "Implementación de IA. En Azure no hay Ollama: Deterministic."
  type        = string
  default     = "Deterministic"
}
