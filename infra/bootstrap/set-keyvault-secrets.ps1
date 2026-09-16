<#
.SYNOPSIS
    Genera y carga en Key Vault los secretos de aplicación de V2 (excepto la connection string).

.DESCRIPTION
    - jwt-key: 64 bytes aleatorios en hexadecimal.
    - seed-admin-password: contraseña del admin que crea el seed inicial (run-seed.ps1).

    Los valores se generan en memoria y se envían por REST: nunca se imprimen, ni pasan por
    argumentos, ni se escriben a disco. Si un secreto ya existe no se toca, salvo con -Rotate.

    La connection string la carga create-db-role.ps1, que es quien conoce la contraseña del rol.

    Requiere que Terraform ya haya creado el Key Vault.
#>
[CmdletBinding()]
param(
    [string] $VaultName     = 'kv-turisclick-v2-dev',
    [string] $ResourceGroup = 'rg-turisclick-dev',
    [switch] $Rotate
)

. "$PSScriptRoot\common.ps1"

Grant-KeyVaultSecretsOfficer -VaultName $VaultName -ResourceGroup $ResourceGroup

$secrets = [ordered]@{
    'jwt-key'             = { New-RandomHex 64 }
    'seed-admin-password' = { New-StrongPassword 32 }
}

foreach ($name in $secrets.Keys) {
    if ((Test-KeyVaultSecret -VaultName $VaultName -SecretName $name) -and -not $Rotate) {
        Write-Host "$name ya existe: sin cambios (usar -Rotate para regenerarlo)."
        continue
    }
    $value = & $secrets[$name]
    Set-KeyVaultSecretValue -VaultName $VaultName -SecretName $name -Value $value
    $value = $null
    Write-Host "$name cargado en $VaultName."
}

if ($Rotate) {
    Write-Host 'Recordatorio: reiniciar la Web App para que tome los valores nuevos (App Service los cachea hasta 24 h).'
}
