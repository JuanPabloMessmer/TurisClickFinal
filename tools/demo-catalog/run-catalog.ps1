<#
.SYNOPSIS
  Ejecuta un script del catálogo demo (load-catalog.mjs / validate-catalog.mjs) con las contraseñas necesarias.

.DESCRIPTION
  - ADMIN_PASSWORD sale de Key Vault (secreto seed-admin-password), vía Azure CLI con la sesión actual.
  - Las contraseñas de las cuentas demo se generan una sola vez y se guardan cifradas con DPAPI (usuario
    actual de Windows) en %USERPROFILE%\.turisclick-secrets. Nunca se escriben en el repo ni se imprimen.
  - Se pasan sólo como variables de entorno de ESTE proceso y se limpian al terminar.

.EXAMPLE
  .\run-catalog.ps1 load-catalog.mjs
  .\run-catalog.ps1 validate-catalog.mjs
#>
param(
    [Parameter(Mandatory)] [ValidateSet('load-catalog.mjs', 'validate-catalog.mjs')] [string] $Script,
    [string[]] $ScriptArgs = @()
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\..\infra\bootstrap\common.ps1')

$store = Join-Path $env:USERPROFILE '.turisclick-secrets'
if (-not (Test-Path $store)) { New-Item -ItemType Directory -Path $store | Out-Null }

function Get-OrCreateDemoPassword([string] $file) {
    $path = Join-Path $store $file
    if (-not (Test-Path $path)) {
        ConvertTo-SecureString -String (New-StrongPassword 20) -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -Path $path -Encoding utf8
        Write-Host "contraseña generada y guardada (DPAPI): $file"
    }
    Read-DpapiSecret $path
}

$vars = [ordered]@{
    ADMIN_PASSWORD            = { Get-KeyVaultSecretValue -VaultName 'kv-turisclick-v2-dev' -SecretName 'seed-admin-password' }
    PROVIDER_PASSWORD         = { Get-OrCreateDemoPassword 'demo-provider-password.dpapi' }
    CATALOG_PROVIDER_PASSWORD = { Get-OrCreateDemoPassword 'demo-catalog-provider-password.dpapi' }
    DEMO_TOURIST_PASSWORD     = { Get-OrCreateDemoPassword 'demo-tourist-password.dpapi' }
}
try {
    foreach ($name in $vars.Keys) { Set-Item -Path "Env:$name" -Value (& $vars[$name]) }
    & node (Join-Path $PSScriptRoot $Script) @ScriptArgs
    $code = $LASTEXITCODE
}
finally {
    foreach ($name in $vars.Keys) { if (Test-Path "Env:$name") { Remove-Item -Path "Env:$name" } }
}
exit $code
