# User-assigned y no system-assigned: la app referencia secretos de Key Vault desde su
# creación, y la identidad tiene que tener el rol antes de que la app exista.
resource "azurerm_user_assigned_identity" "app" {
  name                = local.app_identity_name
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.location
  tags                = local.tags
}
