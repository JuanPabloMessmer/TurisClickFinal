# Ningún output es sensible: nombres, IDs y URLs públicas.

output "web_app_name" {
  value = azurerm_linux_web_app.api.name
}

output "web_app_url" {
  value = "https://${azurerm_linux_web_app.api.default_hostname}"
}

output "key_vault_name" {
  value = azurerm_key_vault.main.name
}

output "key_vault_uri" {
  value = azurerm_key_vault.main.vault_uri
}

output "app_identity_client_id" {
  value = azurerm_user_assigned_identity.app.client_id
}

output "app_identity_principal_id" {
  value = azurerm_user_assigned_identity.app.principal_id
}

output "database_name" {
  value = azurerm_postgresql_flexible_server_database.v2.name
}

output "postgres_server_fqdn" {
  value = data.azurerm_postgresql_flexible_server.existing.fqdn
}
