# State remoto creado por infra/bootstrap/bootstrap-state.ps1 (fuera de Terraform).
# Acceso sólo por Entra ID: la cuenta tiene las shared keys deshabilitadas.
terraform {
  backend "azurerm" {
    resource_group_name  = "rg-turisclick-tfstate"
    storage_account_name = "stturisclicktfjpm"
    container_name       = "tfstate"
    key                  = "turisclick-v2-dev.tfstate"
    use_azuread_auth     = true
  }
}
