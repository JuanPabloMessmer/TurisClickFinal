data "azurerm_client_config" "current" {}

# Compartido con V1: se lee, nunca se gestiona.
data "azurerm_resource_group" "main" {
  name = var.resource_group_name
}

# Servidor existente, compartido con V1. Deliberadamente NO importado: al no estar en el
# state, ningún plan puede modificarlo ni destruirlo. Tampoco se referencia su base V1.
data "azurerm_postgresql_flexible_server" "existing" {
  name                = var.postgres_server_name
  resource_group_name = var.resource_group_name
}
