<#
.SYNOPSIS
    Aplica un lock CanNotDelete al PostgreSQL Flexible Server compartido por V1 y V2.

.DESCRIPTION
    Protege el servidor y, por herencia, todas sus bases (turisclick_db y turisclick_db_v2)
    contra borrados accidentales desde cualquier herramienta.

    Efecto colateral: también impide borrar reglas de firewall. Por eso se aplica DESPUÉS de
    create-db-role.ps1, run-migrations.ps1 y run-seed.ps1, que abren reglas temporales.

    Se gestiona fuera de Terraform a propósito: la identidad de Terraform no necesita permisos
    para crear o quitar locks.
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-turisclick-dev',
    [string] $ServerName    = 'turisclick-postgres-jpm',
    [string] $LockName      = 'lock-turisclick-postgres-cannotdelete'
)

. "$PSScriptRoot\common.ps1"

$serverId = az postgres flexible-server show --name $ServerName --resource-group $ResourceGroup --query id -o tsv
Assert-Az "leer el servidor $ServerName"

$existing = az lock list --resource $serverId --query "[?name=='$LockName'].name" -o tsv
if ($existing) {
    Write-Host "Lock $LockName ya existía."
} else {
    az lock create --name $LockName --lock-type CanNotDelete --resource $serverId `
        --notes 'Protege el servidor PostgreSQL compartido por TurisClick V1 y V2' -o none
    Assert-Az 'crear lock'
    Write-Host "Lock $LockName (CanNotDelete) aplicado a $ServerName."
}

az lock list --resource $serverId --query '[].{name:name, level:level}' -o table
