# Imágenes que sube el Backoffice (destinos). Fuera del App Service a propósito: el directorio de
# despliegue se reemplaza en cada deploy y las fotos se perderían. Ver infra/README.md, "Imágenes subidas".
#
# - Sin claves: shared keys deshabilitadas; la API escribe con su identidad (rol sobre el contenedor).
# - Lectura pública sólo de blobs (container_access_type = "blob"): las apps descargan cada foto por
#   HTTPS con su URL, pero nadie puede listar el contenedor.
resource "azurerm_storage_account" "images" {
  name                     = var.images_storage_account_name
  resource_group_name      = data.azurerm_resource_group.main.name
  location                 = var.location
  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = "LRS"
  access_tier              = "Hot"

  https_traffic_only_enabled      = true
  min_tls_version                 = "TLS1_2"
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true
  public_network_access           = "Enabled"
  # Necesario para que el contenedor admita lectura anónima de blobs; no expone nada por sí solo.
  allow_nested_items_to_be_public = true

  blob_properties {
    # Un borrado accidental se puede deshacer durante 7 días.
    delete_retention_policy {
      days = 7
    }
    container_delete_retention_policy {
      days = 7
    }
  }

  tags = local.tags

  lifecycle {
    prevent_destroy = true
  }
}

resource "azurerm_storage_container" "images" {
  name                  = var.images_container_name
  storage_account_id    = azurerm_storage_account.images.id
  container_access_type = "blob"
}

# Escritura sólo sobre este contenedor, sólo para la identidad de runtime de la API.
resource "azurerm_role_assignment" "app_images_blob_contributor" {
  scope                = azurerm_storage_container.images.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
  principal_type       = "ServicePrincipal"
}
