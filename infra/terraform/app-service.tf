resource "azurerm_service_plan" "v2" {
  name                = local.service_plan_name
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.location
  os_type             = "Linux"
  sku_name            = var.app_service_sku
  tags                = local.tags
}

resource "azurerm_linux_web_app" "api" {
  name                = var.web_app_name
  resource_group_name = data.azurerm_resource_group.main.name
  location            = var.location
  service_plan_id     = azurerm_service_plan.v2.id

  https_only                    = true
  public_network_access_enabled = true
  client_affinity_enabled       = false

  # Sin publish profiles: el deploy se hará con identidad (OIDC), nunca con credenciales básicas.
  ftp_publish_basic_authentication_enabled       = false
  webdeploy_publish_basic_authentication_enabled = false

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }
  key_vault_reference_identity_id = azurerm_user_assigned_identity.app.id

  site_config {
    always_on               = local.always_on
    ftps_state              = "Disabled"
    http2_enabled           = true
    minimum_tls_version     = "1.2"
    scm_minimum_tls_version = "1.2"

    application_stack {
      dotnet_version = "10.0"
    }
  }

  app_settings = local.app_settings
  tags         = local.tags

  # Las referencias a Key Vault se resuelven al arrancar: el permiso tiene que existir antes.
  depends_on = [azurerm_role_assignment.app_key_vault_secrets_user]
}
