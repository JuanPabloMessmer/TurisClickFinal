<#
.SYNOPSIS
    Crea el backend remoto del Terraform state de TurisClick V2.

.DESCRIPTION
    Resource group propio, Storage Account sin shared keys (sólo Entra ID), versionado,
    soft delete de blobs y containers, container privado y lock CanNotDelete.

    Se crea con az CLI y no con Terraform: el backend del state no puede vivir en el
    mismo state que protege. Idempotente: re-ejecutarlo re-aplica la configuración
    esperada sin recrear nada.

    Nunca lee, guarda ni imprime access keys.
#>
[CmdletBinding()]
param(
    [string] $Location           = 'brazilsouth',
    [string] $ResourceGroupName  = 'rg-turisclick-tfstate',
    [string] $StorageAccountName = 'stturisclicktfjpm',
    [string] $ContainerName      = 'tfstate',
    [int]    $RetentionDays      = 14,
    [string] $LockName           = 'lock-tfstate-cannotdelete'
)

$ErrorActionPreference = 'Stop'

function Assert-Az([string] $What) {
    if ($LASTEXITCODE -ne 0) { throw "Falló: $What" }
}

$tags = @('project=turisclick', 'component=terraform-state', 'managed-by=bootstrap-script')

# --- 0. prerequisitos --------------------------------------------------------------
$storageProvider = az provider show --namespace Microsoft.Storage --query registrationState -o tsv
if ($storageProvider -ne 'Registered') { throw 'Microsoft.Storage no está registrado: correr register-providers.ps1' }

# --- 1. resource group -------------------------------------------------------------
if ((az group exists --name $ResourceGroupName) -eq 'true') {
    $rgLocation = az group show --name $ResourceGroupName --query location -o tsv
    if ($rgLocation -ne $Location) { throw "$ResourceGroupName existe en $rgLocation, no en $Location" }
    Write-Host "Resource group $ResourceGroupName ya existía."
} else {
    az group create --name $ResourceGroupName --location $Location --tags $tags -o none
    Assert-Az "crear $ResourceGroupName"
    Write-Host "Resource group $ResourceGroupName creado."
}

# --- 2. storage account ------------------------------------------------------------
$existing = az storage account list --resource-group $ResourceGroupName --query "[?name=='$StorageAccountName'].name" -o tsv
$hardening = @(
    '--sku', 'Standard_LRS',
    '--min-tls-version', 'TLS1_2',
    '--https-only', 'true',
    '--allow-blob-public-access', 'false',
    # Sin shared keys, Entra ID es el único modo de acceso a los datos.
    '--allow-shared-key-access', 'false',
    '--allow-cross-tenant-replication', 'false',
    '--public-network-access', 'Enabled'
)

if ($existing) {
    az storage account update --name $StorageAccountName --resource-group $ResourceGroupName @hardening -o none
    Assert-Az "re-aplicar configuración a $StorageAccountName"
    Write-Host "Storage account $StorageAccountName ya existía: configuración re-aplicada."
} else {
    $check = az storage account check-name --name $StorageAccountName -o json | ConvertFrom-Json
    if (-not $check.nameAvailable) { throw "El nombre $StorageAccountName no está disponible: $($check.reason)" }

    az storage account create --name $StorageAccountName --resource-group $ResourceGroupName `
        --location $Location --kind StorageV2 --access-tier Hot --tags $tags @hardening -o none
    Assert-Az "crear $StorageAccountName"
    Write-Host "Storage account $StorageAccountName creada."
}

# --- 3. versionado y soft delete (plano de control, no requiere keys) ---------------
az storage account blob-service-properties update --account-name $StorageAccountName --resource-group $ResourceGroupName `
    --enable-versioning true `
    --enable-delete-retention true --delete-retention-days $RetentionDays `
    --enable-container-delete-retention true --container-delete-retention-days $RetentionDays -o none
Assert-Az 'configurar versionado y soft delete'
Write-Host "Versionado y soft delete ($RetentionDays días) configurados."

# --- 4. container privado (vía ARM: no depende de permisos de datos) ----------------
$containerExists = az storage container-rm exists --storage-account $StorageAccountName --resource-group $ResourceGroupName --name $ContainerName -o tsv
if ($containerExists -eq 'true') {
    Write-Host "Container $ContainerName ya existía."
} else {
    az storage container-rm create --storage-account $StorageAccountName --resource-group $ResourceGroupName `
        --name $ContainerName --public-access off -o none
    Assert-Az "crear container $ContainerName"
    Write-Host "Container $ContainerName creado."
}

# --- 5. acceso a datos por Entra ID para quien ejecuta ------------------------------
$accountId = az storage account show --name $StorageAccountName --resource-group $ResourceGroupName --query id -o tsv
$me = az ad signed-in-user show --query id -o tsv
Assert-Az 'obtener el usuario actual'
$role = 'Storage Blob Data Contributor'
# Salida tsv a propósito: az.cmd pasa por cmd.exe y rompe JMESPath con paréntesis, y en
# PowerShell 5.1 ConvertFrom-Json de "[]" devuelve un único objeto, así que contar daría 1.
$assignedIds = az role assignment list --assignee $me --scope $accountId --role $role --query '[].id' -o tsv
Assert-Az "listar asignaciones de $role"
if ($assignedIds) {
    Write-Host "El usuario actual ya tenía '$role'."
} else {
    az role assignment create --assignee-object-id $me --assignee-principal-type User --role $role --scope $accountId -o none
    Assert-Az "asignar $role"
    Write-Host "Asignado '$role' al usuario actual."
}

# --- 6. verificar lectura y escritura por Entra ID ----------------------------------
# La asignación de roles puede tardar unos minutos en propagarse.
$probe = '.access-probe'
$ok = $false
# Mientras el rol se propaga, az falla y escribe en stderr: con 'Stop' eso cortaría el reintento.
$ErrorActionPreference = 'Continue'
for ($i = 1; $i -le 30 -and -not $ok; $i++) {
    $null = az storage blob list --account-name $StorageAccountName --container-name $ContainerName --auth-mode login --num-results 1 -o none 2>&1
    if ($LASTEXITCODE -eq 0) { $ok = $true } else { Write-Host "  esperando propagación del rol ($i/30)..."; Start-Sleep -Seconds 10 }
}
$ErrorActionPreference = 'Stop'
if (-not $ok) { throw 'Sin acceso de lectura por Entra ID tras 5 minutos' }
Write-Host 'Lectura por Entra ID: OK.'

# La propagación puede habilitar la lectura antes que la escritura: también se reintenta.
$ok = $false
$ErrorActionPreference = 'Continue'
for ($i = 1; $i -le 30 -and -not $ok; $i++) {
    $null = az storage blob upload --account-name $StorageAccountName --container-name $ContainerName --auth-mode login `
        --name $probe --data 'probe' --overwrite -o none 2>&1
    if ($LASTEXITCODE -eq 0) { $ok = $true } else { Write-Host "  esperando permiso de escritura ($i/30)..."; Start-Sleep -Seconds 10 }
}
$ErrorActionPreference = 'Stop'
if (-not $ok) { throw 'Sin acceso de escritura por Entra ID tras 5 minutos' }
az storage blob delete --account-name $StorageAccountName --container-name $ContainerName --auth-mode login --name $probe -o none
Assert-Az 'borrar blob de prueba por Entra ID'
Write-Host 'Escritura y borrado por Entra ID: OK.'

# --- 7. lock --------------------------------------------------------------------
$lock = az lock list --resource-group $ResourceGroupName --query "[?name=='$LockName'].name" -o tsv
if ($lock) {
    Write-Host "Lock $LockName ya existía."
} else {
    az lock create --name $LockName --lock-type CanNotDelete --resource-group $ResourceGroupName `
        --notes 'Protege el Terraform state de TurisClick V2' -o none
    Assert-Az 'crear lock'
    Write-Host "Lock $LockName (CanNotDelete) creado sobre $ResourceGroupName."
}

Write-Host "`nBackend listo: $StorageAccountName / $ContainerName"
