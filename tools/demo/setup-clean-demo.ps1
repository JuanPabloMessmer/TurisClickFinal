<#
.SYNOPSIS
  Prepara una base de datos de demostración limpia y reproducible para TurisClick V2.

.DESCRIPTION
  La base de desarrollo comparte servidor con las pruebas de integración y los E2E, así que acumula residuos
  (empresas "Operador E2E", categorías "Interés-Ai-…", experiencias de fixtures). No molestan para trabajar,
  pero se ven en la demo: aparecen en el catálogo global del administrador y en el selector de categorías del
  operador. La salida correcta no es borrar esos registros —eso sería destructivo y además no es donde está el
  problema— sino usar una base aparte que nace vacía.

  Este script:
    1. Verifica contra qué servidor y contra qué base va a escribir, y se niega si no es una base de demo.
    2. Crea la base si no existe (nunca la borra sin que se lo pidan explícitamente con -Recreate).
    3. Aplica las migraciones de EF Core que ya están en el repositorio.
    4. Levanta la API apuntada a esa base, con el seed de desarrollo activado.
    5. Carga el catálogo demo curado con las herramientas que ya existen (tools/demo-catalog).
    6. Valida el resultado y resume lo que quedó cargado.

  Es idempotente: volver a correrlo sobre la misma base no duplica nada. El calendario del catálogo es
  relativo a hoy, así que tampoco se vence.

.PARAMETER Database
  Nombre de la base de demo. Tiene que contener "demo" y no puede ser una de las bases protegidas.

.PARAMETER Recreate
  Borra y vuelve a crear la base. Pide confirmación escrita: no hay forma de perder datos sin tipearlo.

.PARAMETER ConfirmName
  El nombre de la base, para confirmar -Recreate sin que haya nadie tipeando (CI, automatización). Tiene que
  coincidir exactamente con -Database: sigue siendo imposible borrar la base equivocada por un tipeo.

.PARAMETER SkipCatalog
  Sólo prepara el esquema (crear base + migraciones), sin cargar el catálogo.

.PARAMETER ApiPort
  Puerto donde este script levanta la API para cargar el catálogo. No toca la API que ya tengas corriendo.

.EXAMPLE
  .\tools\demo\setup-clean-demo.ps1
  Crea turisclick_v2_demo si no existe, migra y carga el catálogo.

.EXAMPLE
  .\tools\demo\setup-clean-demo.ps1 -Recreate
  Empieza de cero (pide confirmación escrita).

.NOTES
  NUNCA toca V1 (turisclick_db) ni la base de Azure. Los guardas de abajo son explícitos a propósito.
#>
[CmdletBinding()]
param(
    [string] $Database = 'turisclick_v2_demo',
    [switch] $Recreate,
    [string] $ConfirmName,
    [switch] $SkipCatalog,
    [int] $ApiPort = 5290
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$apiProject = Join-Path $repoRoot 'src\TurisClick.Api'

# ─────────────────────────── guardas ───────────────────────────
# Bases que este script no puede tocar bajo ninguna circunstancia. turisclick_db es V1: tiene los datos del
# sistema anterior y no se migra ni se reescribe. Las otras dos son el entorno de trabajo y el de pruebas.
$protegidas = @('turisclick_db', 'turisclick_v2_dev', 'turisclick_v2_test', 'postgres', 'template0', 'template1')

if ($protegidas -contains $Database.ToLower()) {
    throw "La base '$Database' está protegida. Este script sólo escribe en bases de demostración."
}
if ($Database -notmatch 'demo') {
    throw "El nombre '$Database' no parece una base de demo. Tiene que contener 'demo' — es el único seguro contra un tipeo."
}

Write-Host "== Base objetivo: $Database ==" -ForegroundColor Cyan

# ─────────────────────────── conexión ───────────────────────────
# La cadena de desarrollo da el servidor y las credenciales; la base se reemplaza por la de demo. No se
# imprime ningún valor.
$secretos = & dotnet user-secrets list --project $apiProject
$csDev = ($secretos | Where-Object { $_ -like 'ConnectionStrings:DefaultConnection*' }) -replace '^[^=]+=\s*', ''
if (-not $csDev) { throw "No hay ConnectionStrings:DefaultConnection en user-secrets de $apiProject." }

$partes = @{}
foreach ($p in $csDev -split ';') { if ($p -match '^\s*([^=]+)=(.*)$') { $partes[$matches[1].Trim().ToLower()] = $matches[2].Trim() } }

$pgHost = $partes['host']
$pgPort = if ($partes['port']) { $partes['port'] } else { '5432' }
$pgUser = $partes['username']

if ($pgHost -notin @('localhost', '127.0.0.1', '::1')) {
    throw "El servidor '$pgHost' no es local. Este script está pensado para una demo local; no toca servidores remotos."
}

$csDemo = ($csDev -split ';' | ForEach-Object {
    if ($_ -match '^\s*[Dd]atabase\s*=') { "Database=$Database" } else { $_ }
}) -join ';'

$psql = @(
    'C:\Program Files\PostgreSQL\16\bin\psql.exe',
    'C:\Program Files\PostgreSQL\17\bin\psql.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $psql) { throw 'No se encontró psql.exe (PostgreSQL 16 o 17).' }

$env:PGHOST = $pgHost
$env:PGPORT = $pgPort
$env:PGUSER = $pgUser
$env:PGPASSWORD = $partes['password']
$env:PGDATABASE = 'postgres'

function Invoke-Psql([string] $sql, [string] $database = 'postgres') {
    $archivo = Join-Path ([System.IO.Path]::GetTempPath()) ("turisclick-demo-{0}.sql" -f [guid]::NewGuid())
    try {
        Set-Content -Path $archivo -Value $sql -Encoding utf8
        $anterior = $env:PGDATABASE
        $env:PGDATABASE = $database
        $salida = & $psql -At -F ' | ' -v ON_ERROR_STOP=1 -f $archivo 2>&1
        if ($LASTEXITCODE -ne 0) { throw "psql falló: $salida" }
        $env:PGDATABASE = $anterior
        return $salida
    }
    finally { if (Test-Path $archivo) { Remove-Item $archivo -Force } }
}

try {
    # ─────────────────────────── crear / recrear ───────────────────────────
    $existe = (Invoke-Psql "SELECT count(*) FROM pg_database WHERE datname = '$Database';") -eq '1'

    if ($Recreate -and $existe) {
        Write-Host ""
        Write-Host "-Recreate borra TODO el contenido de '$Database'." -ForegroundColor Yellow

        # Sin nadie tipeando (CI, automatización) la confirmación llega por parámetro. Sigue exigiendo el
        # nombre exacto, que es lo que protege: lo que no se puede es borrar la base equivocada de casualidad.
        $confirmacion = if ($PSBoundParameters.ContainsKey('ConfirmName')) {
            $ConfirmName
        }
        else {
            Read-Host "Escribí el nombre de la base para confirmar"
        }
        if ($confirmacion -ne $Database) { throw 'Confirmación incorrecta: no se borró nada.' }

        Write-Host "Cerrando conexiones a $Database..."
        Invoke-Psql "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$Database' AND pid <> pg_backend_pid();" | Out-Null
        Invoke-Psql "DROP DATABASE ""$Database"";" | Out-Null
        $existe = $false
        Write-Host "Base borrada." -ForegroundColor Yellow
    }

    if (-not $existe) {
        Invoke-Psql "CREATE DATABASE ""$Database"";" | Out-Null
        Write-Host "Base creada: $Database" -ForegroundColor Green
    }
    else {
        Write-Host "La base ya existe; se reutiliza (el cargador es idempotente)."
    }

    # ─────────────────────────── migraciones ───────────────────────────
    Write-Host ""
    Write-Host "== Migraciones de EF Core ==" -ForegroundColor Cyan
    $env:ConnectionStrings__DefaultConnection = $csDemo
    & dotnet ef database update --project $apiProject --no-build 2>&1 | Where-Object { $_ -notmatch '^\s*$' } | Select-Object -Last 6
    if ($LASTEXITCODE -ne 0) {
        # Sin --no-build por si el proyecto no estaba compilado.
        & dotnet ef database update --project $apiProject
        if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update falló.' }
    }

    $aplicadas = Invoke-Psql 'SELECT count(*), max("MigrationId") FROM "__EFMigrationsHistory";' $Database
    Write-Host "Migraciones aplicadas: $aplicadas" -ForegroundColor Green

    if ($SkipCatalog) {
        Write-Host ""
        Write-Host "-SkipCatalog: el esquema está listo y no se cargó catálogo." -ForegroundColor Yellow
        return
    }

    # ─────────────────────────── API + catálogo ───────────────────────────
    Write-Host ""
    Write-Host "== Levantando la API contra $Database (puerto $ApiPort) ==" -ForegroundColor Cyan
    $apiUrl = "http://localhost:$ApiPort"
    $log = Join-Path ([System.IO.Path]::GetTempPath()) 'turisclick-demo-api.log'
    $logErr = "$log.err"
    # Start-Process exige que los archivos de redirección existan cuando la ruta tiene espacios.
    foreach ($f in @($log, $logErr)) { New-Item -ItemType File -Path $f -Force | Out-Null }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    # Start-Process une los argumentos con espacios y NO los entrecomilla: la ruta del proyecto tiene un
    # espacio ("Juan Pablo"), así que va con comillas explícitas o dotnet recibe dos argumentos partidos.
    $api = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', "`"$apiProject`"", '--urls', $apiUrl, '--no-launch-profile') `
        -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError $logErr

    try {
        $listo = $false
        foreach ($i in 1..60) {
            Start-Sleep -Seconds 2
            try {
                $r = Invoke-WebRequest -Uri "$apiUrl/api/categories" -TimeoutSec 5 -UseBasicParsing
                if ($r.StatusCode -eq 200) { $listo = $true; break }
            }
            catch { }
        }
        if (-not $listo) {
            $ultimas = (Get-Content $log -Tail 15 -ErrorAction SilentlyContinue) -join "`n"
            throw "La API no respondió en $apiUrl.`n--- últimas líneas del log ---`n$ultimas"
        }
        Write-Host "API lista. El seed creó el administrador y el catálogo base." -ForegroundColor Green

        Write-Host ""
        Write-Host "== Cargando el catálogo demo curado ==" -ForegroundColor Cyan
        # Se reutiliza run-catalog.ps1, que resuelve las contraseñas demo (DPAPI) sin exponerlas. Para la demo
        # local el administrador sale de user-secrets en vez de Key Vault.
        $almacen = Join-Path $env:USERPROFILE '.turisclick-secrets'
        . (Join-Path $repoRoot 'infra\bootstrap\common.ps1')

        $env:TURISCLICK_API = $apiUrl
        $env:ADMIN_PASSWORD = ($secretos | Where-Object { $_ -like 'Seed:AdminPassword*' }) -replace '^[^=]+=\s*', ''
        $env:PROVIDER_PASSWORD = Read-DpapiSecret (Join-Path $almacen 'demo-provider-password.dpapi')
        $env:CATALOG_PROVIDER_PASSWORD = Read-DpapiSecret (Join-Path $almacen 'demo-catalog-provider-password.dpapi')
        $env:DEMO_TOURIST_PASSWORD = Read-DpapiSecret (Join-Path $almacen 'demo-tourist-password.dpapi')

        & node (Join-Path $repoRoot 'tools\demo-catalog\load-catalog.mjs')
        if ($LASTEXITCODE -ne 0) { throw 'El cargador del catálogo falló.' }

        # ─────────────────────────── resumen ───────────────────────────
        Write-Host ""
        Write-Host "== Qué quedó en $Database ==" -ForegroundColor Cyan
        $resumenSql = @'
SELECT 'empresas aprobadas', count(*)::text FROM companies WHERE status = 'APPROVED'
UNION ALL SELECT 'experiencias publicadas', count(*)::text FROM experiences WHERE status = 'PUBLISHED'
UNION ALL SELECT 'paquetes publicados', count(*)::text FROM packages WHERE status = 'PUBLISHED'
UNION ALL SELECT 'paquetes con politica de cancelacion', count(*)::text FROM packages WHERE cancellation_policy IS NOT NULL
UNION ALL SELECT 'destinos con foto', count(*)::text FROM destinations WHERE image_url IS NOT NULL
UNION ALL SELECT 'categorias', count(*)::text FROM categories
UNION ALL SELECT 'fechas futuras de experiencias', count(*)::text FROM experience_availabilities WHERE date >= CURRENT_DATE
UNION ALL SELECT 'salidas futuras de paquetes', count(*)::text FROM package_availabilities WHERE departure_date >= CURRENT_DATE
ORDER BY 1;
'@
        Invoke-Psql $resumenSql $Database | ForEach-Object { "  $_" }

        Write-Host ""
        Write-Host "Listo. Apuntá la API a esta base con:" -ForegroundColor Green
        Write-Host "  `$env:ConnectionStrings__DefaultConnection = '<cadena de dev con Database=$Database>'"
        Write-Host "  dotnet run --project src/TurisClick.Api --urls http://localhost:5288"
    }
    finally {
        if ($api -and -not $api.HasExited) {
            Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue
            Write-Host "API de carga detenida."
        }
        foreach ($v in 'TURISCLICK_API', 'ADMIN_PASSWORD', 'PROVIDER_PASSWORD', 'CATALOG_PROVIDER_PASSWORD', 'DEMO_TOURIST_PASSWORD') {
            Set-Item -Path "Env:$v" -Value '' -ErrorAction SilentlyContinue
        }
    }
}
finally {
    $env:PGPASSWORD = ''
    $env:ConnectionStrings__DefaultConnection = ''
}
