<#
.SYNOPSIS
    Prepara el rol de PostgreSQL de la app V2 sobre turisclick_db_v2 y carga su connection string
    en Key Vault.

.DESCRIPTION
    - Rol dedicado sin privilegios administrativos: la app V2 nunca usa el admin del servidor.
    - Dueño de turisclick_db_v2 y de su schema public. En Azure Flexible Server el schema public
      de una base nueva pertenece a azure_pg_admin, así que ser dueño de la base NO alcanza para
      que las migraciones creen tablas: hay que transferir también el schema.
    - Nada se otorga ni se revoca sobre turisclick_db (V1), que no se toca.
    - La contraseña del admin se lee del almacén DPAPI local de la Fase A.

    Idempotente, en dos partes:
      1. Permisos y ownership: se aplican en cada ejecución.
      2. Contraseña del rol: sólo se genera si db-connection-string no existe en Key Vault o si se
         pide -RotatePassword. En cualquier otro caso se reutiliza la vigente (leída del secreto)
         y ni el rol ni el secreto cambian.

    La contraseña viaja al servidor como verificador SCRAM y sólo se guarda en Key Vault.

    Abre el firewall sólo para la IP actual y lo cierra siempre. Con -SkipTemporaryFirewall no lo
    toca: quien invoca garantiza la conectividad (por ejemplo, para varias operaciones dentro de
    una única ventana). Correr ANTES de lock-postgres.ps1.
#>
[CmdletBinding()]
param(
    [string] $VaultName          = 'kv-turisclick-v2-dev',
    [string] $ResourceGroup      = 'rg-turisclick-dev',
    [string] $ServerName         = 'turisclick-postgres-jpm',
    [string] $Database           = 'turisclick_db_v2',
    [string] $RoleName           = 'turisclick_v2_app',
    [string] $AdminLogin         = 'turiclickadmin',
    [string] $AdminPasswordPath  = (Join-Path $env:USERPROFILE '.turisclick-secrets\pg-admin-password.dpapi'),
    [int]    $MaximumPoolSize    = 10,
    [switch] $RotatePassword,
    [switch] $SkipTemporaryFirewall
)

. "$PSScriptRoot\common.ps1"

$secretName = 'db-connection-string'

if ($Database -eq $script:V1DatabaseName) { throw 'ABORTADO: este script nunca opera sobre la base V1' }
if ($RoleName -notmatch '^[a-z_][a-z0-9_]*$') { throw 'Nombre de rol inválido' }
if ($Database -notmatch '^[a-z_][a-z0-9_]*$') { throw 'Nombre de base inválido' }
if ($AdminLogin -notmatch '^[a-z_][a-z0-9_]*$') { throw 'Nombre de admin inválido' }

$databases = az postgres flexible-server db list --resource-group $ResourceGroup --server-name $ServerName --query '[].name' -o tsv
Assert-Az 'listar bases del servidor'
if (@($databases) -notcontains $Database) { throw "La base $Database no existe todavía: aplicar Terraform primero" }

$fqdn = az postgres flexible-server show --name $ServerName --resource-group $ResourceGroup --query fullyQualifiedDomainName -o tsv
Assert-Az 'leer el FQDN del servidor'

Grant-KeyVaultSecretsOfficer -VaultName $VaultName -ResourceGroup $ResourceGroup

$secretExists = Test-KeyVaultSecret -VaultName $VaultName -SecretName $secretName
$setPassword = (-not $secretExists) -or $RotatePassword

if ($setPassword) {
    $rolePassword = New-StrongPassword 40
    $verifier = New-ScramVerifier $rolePassword
    Write-Host 'Se generará una contraseña nueva para el rol.'
} else {
    # Reutilizar la contraseña vigente: la connection string guardada es la única fuente.
    $existing = Get-KeyVaultSecretValue -VaultName $VaultName -SecretName $secretName
    Assert-V2ConnectionString -ConnectionString $existing -ExpectedDatabase $Database
    if ((Get-ConnectionStringPart $existing 'username') -ne $RoleName) { throw "El secreto $secretName no corresponde al rol $RoleName" }
    $rolePassword = Get-ConnectionStringPart $existing 'password'
    $existing = $null
    Write-Host "$secretName ya existe: se reutiliza la contraseña vigente (sin cambios en el rol ni en el secreto)."
}

$adminPassword = Read-DpapiSecret $AdminPasswordPath

# Plantillas literales (comillas simples): los '$$' del bloque DO no deben interpolarse, y los
# valores se insertan con String.Replace, que no interpreta '$' como lo haría -replace.
$roleSql = @'
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '__ROLE__') THEN
    ALTER ROLE __ROLE__ WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '__VERIFIER__';
  ELSE
    CREATE ROLE __ROLE__ WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '__VERIFIER__';
  END IF;
END
$$;
'@

# Sobre el catálogo global (conectado a la base 'postgres'): sólo afecta a la base V2.
$ownershipSql = @'
GRANT __ROLE__ TO __ADMIN__;
ALTER DATABASE __DATABASE__ OWNER TO __ROLE__;
REVOKE ALL ON DATABASE __DATABASE__ FROM PUBLIC;
'@

# Conectado a la base V2: el schema public pasa a ser del rol.
$schemaSql = @'
ALTER SCHEMA public OWNER TO __ROLE__;
'@

$fill = {
    param([string] $Template)
    $Template.Replace('__ROLE__', $RoleName).Replace('__ADMIN__', $AdminLogin).Replace('__DATABASE__', $Database)
}

$work = {
    if ($setPassword) {
        $result = Invoke-PsqlStdin -HostName $fqdn -Database 'postgres' -User $AdminLogin -Password $adminPassword `
            -Sql ((& $fill $roleSql).Replace('__VERIFIER__', $verifier))
        if ($result.ExitCode -ne 0) { throw "Falló la creación del rol: $($result.Output -join ' | ')" }
        Write-Host "Rol $RoleName creado o con contraseña nueva."
    } else {
        $exists = Invoke-PsqlStdin -HostName $fqdn -Database 'postgres' -User $AdminLogin -Password $adminPassword `
            -Sql "select count(*) from pg_roles where rolname = '$RoleName';"
        if ($exists.ExitCode -ne 0 -or ($exists.Output -join '') -ne '1') {
            throw "El secreto existe pero el rol $RoleName no: usar -RotatePassword para recrearlo"
        }
    }

    $result = Invoke-PsqlStdin -HostName $fqdn -Database 'postgres' -User $AdminLogin -Password $adminPassword -Sql (& $fill $ownershipSql)
    if ($result.ExitCode -ne 0) { throw "Falló la asignación de ownership de la base: $($result.Output -join ' | ')" }

    $result = Invoke-PsqlStdin -HostName $fqdn -Database $Database -User $AdminLogin -Password $adminPassword -Sql (& $fill $schemaSql)
    if ($result.ExitCode -ne 0) { throw "Falló la asignación del schema public: $($result.Output -join ' | ')" }
    Write-Host "$RoleName es dueño de $Database y de su schema public."

    # Verificación con el propio rol: conexión, ownership y CREATE real con un objeto descartable.
    $probe = 'zz_create_probe_' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $check = Invoke-PsqlStdin -HostName $fqdn -Database $Database -User $RoleName -Password $rolePassword -Sql @"
select 'conectado=' || current_user || '@' || current_database();
select 'owner_base=' || pg_get_userbyid(datdba) from pg_database where datname = current_database();
select 'owner_schema_public=' || pg_get_userbyid(nspowner) from pg_namespace where nspname = 'public';
begin;
create table public.$probe (id integer);
drop table public.$probe;
commit;
select 'create_en_public=ok';
select 'objetos_de_prueba_restantes=' || count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public' and c.relname like 'zz_create_probe_%';
"@
    if ($check.ExitCode -ne 0) { throw "La verificación con el rol falló: $($check.Output -join ' | ')" }
    $check.Output | ForEach-Object { Write-Host "  $_" }
}

try {
    if ($SkipTemporaryFirewall) {
        & $work
    } else {
        Invoke-WithTemporaryFirewallRule -ServerName $ServerName -ResourceGroup $ResourceGroup -Action $work
    }

    if ($setPassword) {
        $connectionString = "Host=$fqdn;Port=5432;Database=$Database;Username=$RoleName;Password=$rolePassword;SSL Mode=Require;Maximum Pool Size=$MaximumPoolSize"
        Assert-V2ConnectionString -ConnectionString $connectionString -ExpectedDatabase $Database
        Set-KeyVaultSecretValue -VaultName $VaultName -SecretName $secretName -Value $connectionString
        Write-Host "$secretName cargado en $VaultName."
    }
} finally {
    $adminPassword = $null; $rolePassword = $null; $verifier = $null; $connectionString = $null
}
