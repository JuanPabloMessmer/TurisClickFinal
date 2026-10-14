<#
.SYNOPSIS
    Respaldo lógico verificado de turisclick_db_v2 (y sólo de esa base), con prueba de restauración.

.DESCRIPTION
    Usado en la Oleada 15 antes de aplicar las migraciones 0010-0014. Sólo LEE de Azure:

      1. Abre una regla de firewall temporal sólo para la IP pública actual y la cierra siempre,
         verificando que el firewall quede idéntico (Invoke-WithTemporaryFirewallRule).
      2. Se conecta como turisclick_v2_app (dueño de la base V2, no el admin del servidor) y prueba la
         identidad: si current_database() no es turisclick_db_v2, aborta antes de volcar nada.
      3. pg_dump en formato custom de turisclick_db_v2. Nunca toca turisclick_db (V1).
      4. Verifica el archivo: tamaño, SHA-256 y pg_restore --list.
      5. Lo restaura en una base descartable del PostgreSQL LOCAL, compara los conteos de filas de cada
         tabla contra Azure y borra la base descartable.

    Ningún secreto se imprime, se pasa por argumentos ni se escribe a disco: la connection string se lee de
    Key Vault en memoria y la contraseña viaja por PGPASSWORD. El respaldo se guarda FUERA del repositorio,
    en una carpeta con acceso sólo para el usuario actual.

.EXAMPLE
    .\infra\bootstrap\backup-v2-database.ps1 -Label antes-de-0015
#>
[CmdletBinding()]
param(
    [string] $Label = 'manual',
    [string] $BackupRoot = (Join-Path $env:USERPROFILE 'turisclick-backups')
)

. "$PSScriptRoot\common.ps1"

$Expected = 'turisclick_db_v2'
$ExpectedHost = 'turisclick-postgres-jpm.postgres.database.azure.com'
$pgBin = 'C:\Program Files\PostgreSQL\16\bin'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

if ($Label -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Label inválido' }
if ([IO.Path]::GetFullPath($BackupRoot).StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ABORTADO: el respaldo no puede guardarse dentro del repositorio'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dump = Join-Path $BackupRoot "turisclick_db_v2-$Label-$stamp.dump"
$report = Join-Path $BackupRoot "turisclick_db_v2-$Label-$stamp.report.txt"

# Conteo exacto por tabla: es lo que se compara entre el origen y la restauración.
$countsSql = @'
select table_name || '=' || (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text
from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by table_name;
'@

$identitySql = @'
select 'current_database=' || current_database();
select 'current_user=' || current_user;
select 'server_version=' || version();
select 'is_superuser=' || current_setting('is_superuser');
select 'v1_create_priv=' || has_database_privilege('turisclick_db', 'CREATE');
select 'migration=' || "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";
select 'db_size=' || pg_size_pretty(pg_database_size(current_database()));
'@

New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
icacls $BackupRoot /inheritance:r /grant:r "${env:USERNAME}:(OI)(CI)F" | Out-Null

$cs = Get-KeyVaultSecretValue -VaultName 'kv-turisclick-v2-dev' -SecretName 'db-connection-string'
Assert-V2ConnectionString -ConnectionString $cs -ExpectedDatabase $Expected
$hostName = Get-ConnectionStringPart $cs 'host'
$user = Get-ConnectionStringPart $cs 'username'
$pw = Get-ConnectionStringPart $cs 'password'
$cs = $null
if ($hostName -ne $ExpectedHost) { throw 'ABORTADO: host inesperado' }

$script:sourceCounts = $null
try {
    Invoke-WithTemporaryFirewallRule -ServerName 'turisclick-postgres-jpm' -ResourceGroup 'rg-turisclick-dev' -Action {
        $id = Invoke-PsqlStdin -HostName $hostName -Database $Expected -User $user -Password $pw -Sql $identitySql
        if ($id.ExitCode -ne 0) { throw "No se pudo verificar la identidad de la base: $($id.Output -join ' | ')" }
        $id.Output | ForEach-Object { Write-Host "  $_" }
        if (($id.Output | Where-Object { $_ -like 'current_database=*' }) -ne "current_database=$Expected") {
            throw 'ABORTADO: la base conectada no es turisclick_db_v2'
        }
        $id.Output | Set-Content -Encoding utf8 $report

        $env:PGPASSWORD = $pw; $env:PGCONNECT_TIMEOUT = '20'
        try {
            & "$pgBin\pg_dump.exe" -d "host=$hostName port=5432 dbname=$Expected user=$user sslmode=require" `
                --format=custom --compress=6 --no-password --file=$dump
            if ($LASTEXITCODE -ne 0) { throw 'pg_dump falló' }
        } finally { $env:PGPASSWORD = $null }

        $c = Invoke-PsqlStdin -HostName $hostName -Database $Expected -User $user -Password $pw -Sql $countsSql
        if ($c.ExitCode -ne 0) { throw 'No se pudieron contar las filas del origen' }
        $script:sourceCounts = $c.Output
    }
} finally { $pw = $null }

# ---- integridad del archivo ----
$size = (Get-Item $dump).Length
if ($size -le 0) { throw 'El respaldo está vacío' }
$hash = (Get-FileHash -Algorithm SHA256 $dump).Hash
& "$pgBin\pg_restore.exe" --list $dump > "$dump.toc.txt"
if ($LASTEXITCODE -ne 0) { throw 'pg_restore --list no pudo leer el respaldo' }
Write-Host "Respaldo: $dump ($size bytes) SHA256=$hash"

# ---- prueba de restauración en una base descartable LOCAL ----
$secrets = dotnet user-secrets list --project (Join-Path $repoRoot 'src\TurisClick.Api')
$local = ($secrets | Where-Object { $_ -like 'ConnectionStrings:DefaultConnection*' }) -replace '^[^=]+=\s*', ''
$lhost = Get-ConnectionStringPart $local 'host'
$luser = Get-ConnectionStringPart $local 'username'
$env:PGPASSWORD = Get-ConnectionStringPart $local 'password'
$local = $null; $secrets = $null
if ($lhost -notin @('localhost', '127.0.0.1')) { throw 'ABORTADO: la prueba de restauración sólo corre contra localhost' }

$checkDb = "turisclick_v2_restorecheck_$($stamp -replace '-', '_')"
try {
    & "$pgBin\createdb.exe" -h $lhost -U $luser --no-password $checkDb
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la base local de prueba' }
    & "$pgBin\pg_restore.exe" -h $lhost -U $luser --no-password -d $checkDb --no-owner --no-privileges --exit-on-error $dump
    if ($LASTEXITCODE -ne 0) { throw 'La restauración local falló' }
    $restored = $countsSql | & "$pgBin\psql.exe" -h $lhost -U $luser --no-password -d $checkDb -X -q -A -t -v ON_ERROR_STOP=1 -f -
    $diff = Compare-Object @($script:sourceCounts) @($restored)
    if ($diff) { $diff | Format-Table | Out-String | Write-Host; throw 'Los conteos restaurados NO coinciden con el origen' }
    Write-Host "Restauración verificada: $(@($restored).Count) tablas, conteos idénticos al origen."
    Add-Content -Encoding utf8 $report (@("sha256=$hash", "bytes=$size", '--- conteos (origen == restaurado) ---') + @($restored))
} finally {
    & "$pgBin\dropdb.exe" -h $lhost -U $luser --no-password --if-exists $checkDb
    $env:PGPASSWORD = $null
}
Write-Host "Reporte: $report"
