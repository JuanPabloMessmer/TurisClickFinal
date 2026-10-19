locals {
  # Todos los nombres V2 llevan "v2" para que nunca se confundan con los recursos de V1.
  service_plan_name = "asp-turisclick-v2-${var.environment}"
  app_identity_name = "id-turisclick-v2-app"

  # Nombres de los secretos en Key Vault. Los valores se cargan con los scripts de
  # infra/bootstrap: Terraform sólo conoce los nombres, nunca los valores.
  key_vault_secret_names = {
    db_connection_string = "db-connection-string"
    jwt_key              = "jwt-key"
    duffel_test_token    = "duffel-test-token"
  }

  # Duffel sólo si se pide explícitamente. Va en Terraform —no con `az webapp config appsettings set`— porque
  # Terraform reescribe todos los app settings en cada apply: uno cargado a mano se borraría en el siguiente.
  # El token nunca pasa por acá: es una referencia a Key Vault, y DuffelFlightProvider rechaza cualquier token
  # que no sea `duffel_test_`.
  flights_app_settings = var.flights_provider == "Duffel" ? {
    Flights__Provider            = "Duffel"
    Flights__Duffel__AccessToken = "@Microsoft.KeyVault(VaultName=${var.key_vault_name};SecretName=${local.key_vault_secret_names.duffel_test_token})"
  } : {}

  # Always On no está disponible en los planes gratuitos.
  always_on = !contains(["F1", "D1"], var.app_service_sku)

  app_settings = merge(
    {
      ASPNETCORE_ENVIRONMENT            = "Production"
      Ai__Provider                      = var.ai_provider
      Reservations__Expiration__Enabled = "true"

      # Sólo referencias: el valor real vive en Key Vault y App Service lo resuelve con la
      # identidad de la app. Ningún secreto entra en la configuración ni en el state.
      ConnectionStrings__DefaultConnection = "@Microsoft.KeyVault(VaultName=${var.key_vault_name};SecretName=${local.key_vault_secret_names.db_connection_string})"
      Jwt__Key                             = "@Microsoft.KeyVault(VaultName=${var.key_vault_name};SecretName=${local.key_vault_secret_names.jwt_key})"

      # Imágenes subidas: Blob Storage con la identidad de la app. Ninguna clave: sólo el endpoint público.
      ImageStorage__Provider                           = "AzureBlob"
      ImageStorage__AzureBlob__ServiceUri              = azurerm_storage_account.images.primary_blob_endpoint
      ImageStorage__AzureBlob__ContainerName           = azurerm_storage_container.images.name
      ImageStorage__AzureBlob__ManagedIdentityClientId = azurerm_user_assigned_identity.app.client_id
    },
    { for index, origin in var.cors_allowed_origins : "Cors__AllowedOrigins__${index}" => origin },
    local.flights_app_settings,
  )

  tags = {
    project     = "turisclick"
    version     = "v2"
    environment = var.environment
    managed-by  = "terraform"
  }
}
