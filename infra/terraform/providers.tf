# La subscription se toma de ARM_SUBSCRIPTION_ID (ver infra/README.md): no se versiona.
provider "azurerm" {
  # Explícito aunque sea el default de 5.x: los providers se registran a mano
  # (infra/bootstrap/register-providers.ps1), nunca como efecto colateral de un plan.
  resource_provider_registrations = "none"
  storage_use_azuread             = true

  features {
    key_vault {
      # Un destroy nunca debe purgar el vault: queda recuperable durante el soft delete.
      purge_soft_delete_on_destroy    = false
      recover_soft_deleted_key_vaults = true
    }

    resource_group {
      prevent_deletion_if_contains_resources = true
    }
  }
}
