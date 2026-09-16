# Terraform crea el vault y los permisos, pero NUNCA secretos: un azurerm_key_vault_secret
# (o un random_password) dejaría el valor en el state. Los secretos se cargan con
# infra/bootstrap/set-keyvault-secrets.ps1 y create-db-role.ps1.
resource "azurerm_key_vault" "main" {
  name                = var.key_vault_name
  location            = var.location
  resource_group_name = data.azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  rbac_authorization_enabled    = true
  public_network_access_enabled = true
  soft_delete_retention_days    = 90

  # Activarla es irreversible e impide reutilizar el nombre durante la retención. Queda
  # apagada para el ambiente de demo; prevent_destroy y el soft delete cubren el borrado.
  purge_protection_enabled = false

  tags = local.tags

  lifecycle {
    prevent_destroy = true
  }
}

# Sólo lectura de secretos para la identidad de la app.
resource "azurerm_role_assignment" "app_key_vault_secrets_user" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
  principal_type       = "ServicePrincipal"
}
