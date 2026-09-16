<#
.SYNOPSIS
    Identidad de deploy de GitHub Actions para TurisClick V2 (OIDC, sin secretos).

.DESCRIPTION
    - User-assigned managed identity dedicada al CI/CD: separada de id-turisclick-v2-app, que es la
      identidad de runtime de la app y la única con acceso a Key Vault.
    - Federated credential: sólo tokens OIDC emitidos por GitHub para pushes/dispatch sobre la rama
      master de este repositorio. Sin client secret, sin publish profile.
    - RBAC mínimo: Website Contributor ÚNICAMENTE sobre app-turisclick-v2-api. Sin acceso a
      PostgreSQL, Key Vault, V1 ni a la subscription.

    Idempotente. Imprime sólo identificadores públicos (client id / tenant id), que el workflow
    necesita y que no son secretos: la seguridad la dan el subject federado y el alcance del rol.
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-turisclick-dev',
    [string] $Location      = 'brazilsouth',
    [string] $IdentityName  = 'id-turisclick-v2-github-deploy',
    [string] $WebAppName    = 'app-turisclick-v2-api',
    [string] $Repository    = 'JuanPabloMessmer/TurisClickFinal',
    [string] $Branch        = 'master'
)

. "$PSScriptRoot\common.ps1"

$tags = @('project=turisclick', 'version=v2', 'environment=dev', 'component=cicd', 'managed-by=bootstrap-script')

# --- identidad ---------------------------------------------------------------------
$existing = az identity list --resource-group $ResourceGroup --query "[?name=='$IdentityName'].name" -o tsv
if ($existing) {
    Write-Host "Identidad $IdentityName ya existía."
} else {
    az identity create --name $IdentityName --resource-group $ResourceGroup --location $Location --tags $tags -o none
    Assert-Az "crear $IdentityName"
    Write-Host "Identidad $IdentityName creada."
}
$identity = az identity show --name $IdentityName --resource-group $ResourceGroup -o json | ConvertFrom-Json

# --- federated credential ------------------------------------------------------------
$credentialName = "github-$($Repository.Replace('/', '-'))-$Branch"
$subject = "repo:${Repository}:ref:refs/heads/$Branch"
$credential = az identity federated-credential list --identity-name $IdentityName --resource-group $ResourceGroup --query "[?name=='$credentialName'].subject" -o tsv
if ($credential -eq $subject) {
    Write-Host "Federated credential $credentialName ya existía."
} elseif ($credential) {
    throw "El federated credential $credentialName existe con otro subject: revisarlo manualmente"
} else {
    az identity federated-credential create --name $credentialName --identity-name $IdentityName --resource-group $ResourceGroup `
        --issuer 'https://token.actions.githubusercontent.com' --subject $subject --audiences 'api://AzureADTokenExchange' -o none
    Assert-Az 'crear federated credential'
    Write-Host "Federated credential creado para $subject."
}

# --- RBAC mínimo -----------------------------------------------------------------------
$webAppId = az webapp show --name $WebAppName --resource-group $ResourceGroup --query id -o tsv
Assert-Az "leer $WebAppName"
$role = 'Website Contributor'
$assigned = az role assignment list --assignee $identity.principalId --scope $webAppId --role $role --query '[].id' -o tsv
if ($assigned) {
    Write-Host "'$role' sobre $WebAppName ya estaba asignado."
} else {
    az role assignment create --assignee-object-id $identity.principalId --assignee-principal-type ServicePrincipal `
        --role $role --scope $webAppId -o none
    Assert-Az "asignar $role"
    Write-Host "Asignado '$role' sólo sobre $WebAppName."
}

Write-Host "`nIdentificadores para el workflow (no son secretos):"
Write-Host "  AZURE_CLIENT_ID      = $($identity.clientId)"
Write-Host "  AZURE_TENANT_ID      = $($identity.tenantId)"
Write-Host "  AZURE_SUBSCRIPTION_ID= $(az account show --query id -o tsv)"
Write-Host "Asignaciones de la identidad en toda la subscription:"
az role assignment list --assignee $identity.principalId --all --query '[].{role:roleDefinitionName, scope:scope}' -o table
