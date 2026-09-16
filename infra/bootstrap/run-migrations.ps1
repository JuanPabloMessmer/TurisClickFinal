<#
.SYNOPSIS
    Aplica las migraciones EF Core de V2 sobre turisclick_db_v2.

.DESCRIPTION
    Construye un migrations bundle y lo ejecuta con la connection string del rol de la app,
    leída de Key Vault. La connection string se pasa por variable de entorno del proceso
    (el DbContext la lee de ConnectionStrings:DefaultConnection), nunca por argumentos.

    Protecciones: se aborta si la connection string no apunta exactamente a la base V2.
    Nunca se ejecuta contra turisclick_db (V1): sus migraciones son incompatibles.

    Requiere dotnet-ef. Correr ANTES de lock-postgres.ps1.
#>
[CmdletBinding()]
param(
    [string] $VaultName        = 'kv-turisclick-v2-dev',
    [string] $ResourceGroup    = 'rg-turisclick-dev',
    [string] $ServerName       = 'turisclick-postgres-jpm',
    [string] $ExpectedDatabase = 'turisclick_db_v2'
)

. "$PSScriptRoot\common.ps1"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $repoRoot 'src\TurisClick.Api\TurisClick.Api.csproj'
$bundleDir = Join-Path ([IO.Path]::GetTempPath()) ('turisclick-efbundle-' + [Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $bundleDir 'efbundle.exe'

$connectionString = Get-KeyVaultSecretValue -VaultName $VaultName -SecretName 'db-connection-string'
Assert-V2ConnectionString -ConnectionString $connectionString -ExpectedDatabase $ExpectedDatabase

Write-Host 'Construyendo el migrations bundle...'
New-Item -ItemType Directory -Path $bundleDir | Out-Null
dotnet ef migrations bundle --project $project --startup-project $project --configuration Release --output $bundle --force
Assert-Az 'construir el migrations bundle'

try {
    Invoke-WithTemporaryFirewallRule -ServerName $ServerName -ResourceGroup $ResourceGroup -Action {
        $env:ASPNETCORE_ENVIRONMENT = 'Production'
        $env:ConnectionStrings__DefaultConnection = $connectionString
        try {
            & $bundle
            if ($LASTEXITCODE -ne 0) { throw 'Falló la aplicación de migraciones' }
        } finally {
            $env:ConnectionStrings__DefaultConnection = $null
        }

        $hostName = Get-ConnectionStringPart $connectionString 'host'
        $user = Get-ConnectionStringPart $connectionString 'username'
        $password = Get-ConnectionStringPart $connectionString 'password'
        $history = Invoke-PsqlStdin -HostName $hostName -Database $ExpectedDatabase -User $user -Password $password `
            -Sql 'select count(*) from "__EFMigrationsHistory";'
        $password = $null
        if ($history.ExitCode -ne 0) { throw 'No se pudo leer __EFMigrationsHistory' }
        Write-Host "Migraciones aplicadas en ${ExpectedDatabase}: $($history.Output -join '')"
    }
} finally {
    $connectionString = $null
    if (Test-Path $bundleDir) { Remove-Item -Recurse -Force $bundleDir }
}
