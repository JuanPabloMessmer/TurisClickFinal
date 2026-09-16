<#
.SYNOPSIS
    Ejecución ÚNICA del seed de datos de demo (admin, categorías, destinos) sobre turisclick_db_v2.

.DESCRIPTION
    El DevelopmentSeeder sólo corre en Development. En vez de cambiar código, se ejecuta la API
    una vez desde esta máquina, en Development, apuntando a la base V2 de Azure; se espera a que
    termine el seed y se detiene el proceso.

    - Connection string y contraseña del admin se leen de Key Vault y se pasan por variables
      de entorno del proceso hijo: nunca por argumentos ni a disco.
    - Las variables de entorno tienen prioridad sobre los user-secrets locales.
    - La expiración automática de reservas se apaga para este proceso local.
    - La salida del proceso se descarta: sólo se reportan conteos consultados después.

    Idempotente (el seeder comprueba existencia). Correr ANTES de lock-postgres.ps1 y DESPUÉS
    de run-migrations.ps1.

    Abre el firewall sólo para la IP actual y lo cierra siempre. Con -SkipTemporaryFirewall no lo
    toca: quien invoca garantiza la conectividad (por ejemplo, para verificar antes y después dentro
    de una única ventana).
#>
[CmdletBinding()]
param(
    [string] $VaultName        = 'kv-turisclick-v2-dev',
    [string] $ResourceGroup    = 'rg-turisclick-dev',
    [string] $ServerName       = 'turisclick-postgres-jpm',
    [string] $ExpectedDatabase = 'turisclick_db_v2',
    [int]    $TimeoutSeconds   = 240,
    [switch] $SkipTemporaryFirewall
)

. "$PSScriptRoot\common.ps1"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projectDir = Join-Path $repoRoot 'src\TurisClick.Api'

$connectionString = Get-KeyVaultSecretValue -VaultName $VaultName -SecretName 'db-connection-string'
Assert-V2ConnectionString -ConnectionString $connectionString -ExpectedDatabase $ExpectedDatabase
$adminPassword = Get-KeyVaultSecretValue -VaultName $VaultName -SecretName 'seed-admin-password'

Write-Host 'Compilando la API (Release)...'
dotnet build (Join-Path $projectDir 'TurisClick.Api.csproj') -c Release -nologo -v q
Assert-Az 'compilar la API'
$dll = Join-Path $projectDir 'bin\Release\net10.0\TurisClick.Api.dll'

$work = {
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = 'dotnet'
        $start.Arguments = "`"$dll`""
        $start.WorkingDirectory = $projectDir
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
        $start.EnvironmentVariables['ASPNETCORE_URLS'] = 'http://127.0.0.1:5399'
        $start.EnvironmentVariables['ConnectionStrings__DefaultConnection'] = $connectionString
        $start.EnvironmentVariables['Seed__Enabled'] = 'true'
        $start.EnvironmentVariables['Seed__AdminPassword'] = $adminPassword
        $start.EnvironmentVariables['Ai__Provider'] = 'Deterministic'
        $start.EnvironmentVariables['Reservations__Expiration__Enabled'] = 'false'

        $process = [System.Diagnostics.Process]::Start($start)
        # Se descarta la salida de forma asíncrona: leerla bloqueando podría colgar el script, y
        # no dejar vaciar los buffers podría colgar la app.
        $process.BeginOutputReadLine()
        $process.BeginErrorReadLine()

        $listening = $false
        try {
            # El seed corre antes de que la app empiece a escuchar: si responde HTTP, el seed terminó.
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            while (-not $process.HasExited -and -not $listening -and (Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 3
                try {
                    $null = Invoke-WebRequest -Uri 'http://127.0.0.1:5399/api/categories' -UseBasicParsing -TimeoutSec 5
                    $listening = $true
                } catch {
                    # Una respuesta HTTP de error también prueba que la app ya escucha.
                    if ($_.Exception.Response) { $listening = $true }
                }
            }
        } finally {
            if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        }
        if (-not $listening) { throw 'La API no terminó el arranque a tiempo. Revisar src/TurisClick.Api/logs.' }

        $hostName = Get-ConnectionStringPart $connectionString 'host'
        $user = Get-ConnectionStringPart $connectionString 'username'
        $password = Get-ConnectionStringPart $connectionString 'password'
        $counts = Invoke-PsqlStdin -HostName $hostName -Database $ExpectedDatabase -User $user -Password $password -Sql @'
select 'admins=' || count(*) from users where role = 'ADMIN'
union all select 'categorias=' || count(*) from categories
union all select 'destinos=' || count(*) from destinations;
'@
        $password = $null
        if ($counts.ExitCode -ne 0) { throw "No se pudieron verificar los datos: $($counts.Output -join ' | ')" }
        Write-Host "Seed completado en ${ExpectedDatabase}: $($counts.Output -join ', ')"
}

try {
    if ($SkipTemporaryFirewall) {
        & $work
    } else {
        Invoke-WithTemporaryFirewallRule -ServerName $ServerName -ResourceGroup $ResourceGroup -Action $work
    }
} finally {
    $connectionString = $null
    $adminPassword = $null
}
