# Base V2, nueva y separada de V1 en el mismo servidor. El rol que la usa y su ownership se
# crean fuera de Terraform (infra/bootstrap/create-db-role.ps1) para no poner credenciales
# de PostgreSQL en la configuración ni en el state.
resource "azurerm_postgresql_flexible_server_database" "v2" {
  name      = var.database_name
  server_id = data.azurerm_postgresql_flexible_server.existing.id

  # Idénticos a turisclick_db.
  charset   = "UTF8"
  collation = "en_US.utf8"

  lifecycle {
    prevent_destroy = true
  }
}
