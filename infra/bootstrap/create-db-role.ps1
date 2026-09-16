<#
.SYNOPSIS
    Crea el rol de PostgreSQL de la app V2, le da la propiedad de turisclick_db_v2 y carga la
    connection string en Key Vault.

.DESCRIPTION
    - Rol dedicado sin privilegios administrativos: la app V2 nunca usa el admin del servidor.
    - Dueño de turisclick_db_v2 (en PostgreSQL 16 eso le da el schema public que necesitan las
      migraciones). Sin ningún permiso otorgado sobre turisclick_db (V1), que no se toca.
    - La contraseña del rol se envía como verificador SCRAM y sólo se guarda en Key Vault,
      dentro de la connection string.
    - La contraseña del admin se lee del almacén DPAPI local de la Fase A.

    Abre el firewall sólo para la IP actual durante la operación y lo cierra siempre.
    Correr ANTES de lock-postgres.ps1.
#>
[CmdletBinding()]
param(
    [string] $VaultName            = 'kv-turisclick-v2-dev',
    [string] $ResourceGroup        = 'rg-turisclick-dev',
    [string] $ServerName           = 'turisclick-postgres-jpm',
    [string] $Database             = 'turisclick_db_v2',
    [string] $RoleName             = 'turisclick_v2_app',
    [string] $AdminLogin           = 'turiclickadmin',
    [string] $AdminPasswordPath    = (Join-Path $env:USERPROFILE '.turisclick-secrets\pg-admin-password.dpapi'),
    [int]    $MaximumPoolSize      = 10,
    [switch] $RotatePassword
)

. "$PSScriptRoot\common.ps1"

$secretName = 'db-connection-string'

if ($Database -eq $script:V1DatabaseName) { throw 'ABORTADO: este script nunca opera sobre la base V1' }
if ($RoleName -notmatch '^[a-z_][a-z0-9_]*$') { throw 'Nombre de rol inválido' }
if ($Database -notmatch '^[a-z_][a-z0-9_]*$') { throw 'Nombre de base inválido' }

$databases = az postgres flexible-server db list --resource-group $ResourceGroup --server-name $ServerName --query '[].name' -o tsv
Assert-Az 'listar bases del servidor'
if (@($databases) -notcontains $Database) { throw "La base $Database no existe todavía: aplicar Terraform primero" }

$fqdn = az postgres flexible-server show --name $ServerName --resource-group $ResourceGroup --query fullyQualifiedDomainName -o tsv
Assert-Az 'leer el FQDN del servidor'

Grant-KeyVaultSecretsOfficer -VaultName $VaultName -ResourceGroup $ResourceGroup
if ((Test-KeyVaultSecret -VaultName $VaultName -SecretName $secretName) -and -not $RotatePassword) {
    Write-Host "$secretName ya existe: sin cambios (usar -RotatePassword para rotar la contraseña del rol)."
    return
}

$adminPassword = Read-DpapiSecret $AdminPasswordPath
$rolePassword = New-StrongPassword 40
$verifier = New-ScramVerifier $rolePassword

# Plantilla literal (comillas simples): los '$$' del bloque DO no deben interpolarse, y los
# valores se insertan con String.Replace, que no interpreta '$' como lo haría -replace.
$sql = @'
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '__ROLE__') THEN
    ALTER ROLE __ROLE__ WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '__VERIFIER__';
  ELSE
    CREATE ROLE __ROLE__ WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '__VERIFIER__';
  END IF;
END
$$;
GRANT __ROLE__ TO __ADMIN__;
ALTER DATABASE __DATABASE__ OWNER TO __ROLE__;
REVOKE ALL ON DATABASE __DATABASE__ FROM PUBLIC;
'@
$sql = $sql.Replace('__ROLE__', $RoleName).Replace('__ADMIN__', $AdminLogin).Replace('__DATABASE__', $Database).Replace('__VERIFIER__', $verifier)

Invoke-WithTemporaryFirewallRule -ServerName $ServerName -ResourceGroup $ResourceGroup -Action {
    $result = Invoke-PsqlStdin -HostName $fqdn -Database 'postgres' -User $AdminLogin -Password $adminPassword -Sql $sql
    if ($result.ExitCode -ne 0) { throw "Falló la creación del rol: $($result.Output -join ' | ')" }
    Write-Host "Rol $RoleName listo y dueño de $Database."

    $check = Invoke-PsqlStdin -HostName $fqdn -Database $Database -User $RoleName -Password $rolePassword `
        -Sql 'select current_user || ''@'' || current_database();'
    if ($check.ExitCode -ne 0) { throw 'El rol nuevo no pudo conectarse a la base V2' }
    Write-Host "Login verificado: $($check.Output -join '')"
}

$connectionString = "Host=$fqdn;Port=5432;Database=$Database;Username=$RoleName;Password=$rolePassword;SSL Mode=Require;Maximum Pool Size=$MaximumPoolSize"
Assert-V2ConnectionString -ConnectionString $connectionString -ExpectedDatabase $Database
Set-KeyVaultSecretValue -VaultName $VaultName -SecretName $secretName -Value $connectionString
Write-Host "$secretName cargado en $VaultName."

$adminPassword = $null; $rolePassword = $null; $verifier = $null; $sql = $null; $connectionString = $null
