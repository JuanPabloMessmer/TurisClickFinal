<#
.SYNOPSIS
    Genera y carga en Key Vault los secretos de aplicación de V2 que se indiquen explícitamente.

.DESCRIPTION
    Secretos disponibles:
      - jwt-key             : 64 bytes aleatorios (RNG criptográfico), en hexadecimal.
      - seed-admin-password : contraseña del admin que crea el seed inicial (run-seed.ps1).

    db-connection-string NO se gestiona acá: depende de la contraseña del rol de PostgreSQL y la
    carga create-db-role.ps1.

    Sin -Secret no hace nada: la selección tiene que ser explícita para no crear secretos por error.

    Los valores se generan en memoria y se envían por REST: nunca se imprimen, ni pasan por
    argumentos, ni se escriben a disco, ni pasan por Terraform. Idempotente: un secreto que ya
    existe no se toca, salvo con -Rotate.

    Requiere que Terraform ya haya creado el Key Vault.

.EXAMPLE
    .\set-keyvault-secrets.ps1 -Secret jwt-key

.EXAMPLE
    .\set-keyvault-secrets.ps1 -Secret jwt-key, seed-admin-password -Rotate
#>
[CmdletBinding()]
param(
    [ValidateSet('jwt-key', 'seed-admin-password')]
    [string[]] $Secret = @(),

    [string] $VaultName     = 'kv-turisclick-v2-dev',
    [string] $ResourceGroup = 'rg-turisclick-dev',
    [switch] $Rotate
)

. "$PSScriptRoot\common.ps1"

if ($Secret.Count -eq 0) {
    throw 'Sin cambios: indicar explícitamente qué secretos crear, por ejemplo -Secret jwt-key (valores posibles: jwt-key, seed-admin-password).'
}

$generators = @{
    'jwt-key'             = { New-RandomHex 64 }
    'seed-admin-password' = { New-StrongPassword 32 }
}

Grant-KeyVaultSecretsOfficer -VaultName $VaultName -ResourceGroup $ResourceGroup

foreach ($name in ($Secret | Select-Object -Unique)) {
    if ((Test-KeyVaultSecret -VaultName $VaultName -SecretName $name) -and -not $Rotate) {
        Write-Host "$name ya existe: sin cambios (usar -Rotate para regenerarlo)."
        continue
    }
    $value = & $generators[$name]
    try {
        Set-KeyVaultSecretValue -VaultName $VaultName -SecretName $name -Value $value
    } finally {
        $value = $null
    }
    Write-Host "$name cargado en $VaultName."
}

if ($Rotate) {
    Write-Host 'Recordatorio: reiniciar la Web App para que tome los valores nuevos (App Service los cachea hasta 24 h).'
}
