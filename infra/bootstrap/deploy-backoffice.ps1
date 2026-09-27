<#
.SYNOPSIS
    Compila y publica el Backoffice V2 en Azure Static Web Apps.

.DESCRIPTION
    El build de producción apunta siempre al backend V2 de Azure (frontend/apps/backoffice/.env.production
    + src/lib/env.ts). El deployment token se lee de Azure con la sesión actual de az y se pasa por
    variable de entorno del proceso: nunca se imprime, ni se guarda en el repo, ni viaja como argumento.

    El recurso (swa-turisclick-v2-backoffice, SKU Free) se creó con az CLI y NO está en el state de
    Terraform — ver infra/README.md.

.EXAMPLE
    .\deploy-backoffice.ps1
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-turisclick-dev',
    [string] $AppName       = 'swa-turisclick-v2-backoffice',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$appPath = Join-Path $repoRoot 'frontend\apps\backoffice'
$distPath = Join-Path $appPath 'dist'

if (-not $SkipBuild) {
    Write-Host 'Compilando el Backoffice (modo production → API de Azure)...'
    Push-Location $appPath
    try {
        # Vite escribe avisos por stderr (tamaño de chunks); con ErrorActionPreference='Stop' eso
        # aborta el script aunque el build haya salido bien. Se decide por el exit code, no por stderr.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        npm run build 2>&1 | ForEach-Object { "$_" }
        $buildExit = $LASTEXITCODE
        $ErrorActionPreference = $previous
        if ($buildExit -ne 0) { throw 'Falló el build del Backoffice' }
    }
    finally { Pop-Location }
}

if (-not (Test-Path (Join-Path $distPath 'index.html'))) { throw "No se encontró el build en $distPath" }

Assert-Az 'leer el deployment token'
$token = az staticwebapp secrets list --name $AppName --resource-group $ResourceGroup --query "properties.apiKey" -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) { throw 'No se pudo leer el deployment token' }

try {
    $env:SWA_CLI_DEPLOYMENT_TOKEN = $token
    $token = $null
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    npx --yes @azure/static-web-apps-cli@2 deploy $distPath --env production --no-use-keychain 2>&1 | ForEach-Object { "$_" }
    $deployExit = $LASTEXITCODE
    $ErrorActionPreference = $previous
    if ($deployExit -ne 0) { throw 'Falló el deploy a Static Web Apps' }
}
finally {
    $env:SWA_CLI_DEPLOYMENT_TOKEN = $null
}

$hostName = az staticwebapp show --name $AppName --resource-group $ResourceGroup --query "defaultHostname" -o tsv
Write-Host "Backoffice publicado en https://$hostName"
