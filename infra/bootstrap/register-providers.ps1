<#
.SYNOPSIS
    Registra los Resource Providers de Azure que necesita la infraestructura V2.

.DESCRIPTION
    azurerm 5.x no registra providers por su cuenta (resource_provider_registrations = "none"),
    así que se hace una sola vez por subscription, de forma explícita y auditable.
    Idempotente: si ya están registrados, no hace nada.
#>
[CmdletBinding()]
param(
    [string[]] $Namespaces = @('Microsoft.KeyVault', 'Microsoft.Storage')
)

$ErrorActionPreference = 'Stop'

foreach ($ns in $Namespaces) {
    $state = az provider show --namespace $ns --query registrationState -o tsv
    if ($LASTEXITCODE -ne 0) { throw "No se pudo leer el estado de $ns" }

    if ($state -eq 'Registered') {
        Write-Host "$ns ya estaba registrado."
        continue
    }

    Write-Host "Registrando $ns (estado actual: $state)..."
    az provider register --namespace $ns --wait -o none
    if ($LASTEXITCODE -ne 0) { throw "Falló el registro de $ns" }

    $state = az provider show --namespace $ns --query registrationState -o tsv
    if ($state -ne 'Registered') { throw "$ns quedó en estado '$state'" }
    Write-Host "$ns registrado."
}
