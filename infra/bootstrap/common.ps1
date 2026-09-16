<#
.SYNOPSIS
    Funciones compartidas por los scripts de operaciones de TurisClick V2.

.DESCRIPTION
    Se incluye con dot-sourcing (. "$PSScriptRoot\common.ps1"). Reglas que siguen todas:
      - Ningún secreto se imprime, se pasa por argumentos de línea de comandos ni se escribe a disco.
      - Los secretos viajan en memoria: Key Vault por REST, PostgreSQL por stdin y PGPASSWORD.
      - Las reglas temporales de firewall se eliminan siempre (try/finally) y se verifica el resultado.
#>

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$script:Psql = 'C:\Program Files\PostgreSQL\16\bin\psql.exe'
$script:V1DatabaseName = 'turisclick_db'

function Assert-Az([string] $What) {
    if ($LASTEXITCODE -ne 0) { throw "Falló: $What" }
}

# ------------------------------------------------------------------ secretos locales

$script:Rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider

function Get-CryptoInt([int] $Max) {
    # 64 bits aleatorios: el sesgo de módulo es despreciable para los alfabetos usados.
    $buffer = New-Object byte[] 8
    $script:Rng.GetBytes($buffer)
    [int]([BitConverter]::ToUInt64($buffer, 0) % [uint64]$Max)
}

function New-StrongPassword([int] $Length = 40) {
    # Sin ';', '=', comillas ni caracteres especiales de cmd: seguro dentro de connection strings.
    $upper = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'; $lower = 'abcdefghijklmnopqrstuvwxyz'
    $digits = '0123456789'; $symbols = '-_.~'
    $all = $upper + $lower + $digits + $symbols
    $chars = New-Object char[] $Length
    $chars[0] = $upper[(Get-CryptoInt $upper.Length)]
    $chars[1] = $lower[(Get-CryptoInt $lower.Length)]
    $chars[2] = $digits[(Get-CryptoInt $digits.Length)]
    $chars[3] = $symbols[(Get-CryptoInt $symbols.Length)]
    for ($i = 4; $i -lt $Length; $i++) { $chars[$i] = $all[(Get-CryptoInt $all.Length)] }
    for ($i = $Length - 1; $i -gt 0; $i--) {
        $j = Get-CryptoInt ($i + 1)
        $t = $chars[$i]; $chars[$i] = $chars[$j]; $chars[$j] = $t
    }
    -join $chars
}

function New-RandomHex([int] $Bytes = 64) {
    $buffer = New-Object byte[] $Bytes
    $script:Rng.GetBytes($buffer)
    ($buffer | ForEach-Object { $_.ToString('x2') }) -join ''
}

function New-ScramVerifier([string] $Password) {
    # Verificador SCRAM-SHA-256 (RFC 5802/7677): el servidor nunca recibe la contraseña en claro.
    $salt = New-Object byte[] 16
    $script:Rng.GetBytes($salt)
    $kdf = [System.Security.Cryptography.Rfc2898DeriveBytes]::new(
        [Text.Encoding]::UTF8.GetBytes($Password), $salt, 4096, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $salted = $kdf.GetBytes(32)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($salted)
    $clientKey = $hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes('Client Key'))
    $serverKey = $hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes('Server Key'))
    $storedKey = [System.Security.Cryptography.SHA256]::Create().ComputeHash($clientKey)
    'SCRAM-SHA-256$4096:' + [Convert]::ToBase64String($salt) + '$' +
        [Convert]::ToBase64String($storedKey) + ':' + [Convert]::ToBase64String($serverKey)
}

function Read-DpapiSecret([string] $Path) {
    if (-not (Test-Path $Path)) { throw "No existe el secreto local $Path" }
    [System.Net.NetworkCredential]::new('', (Get-Content $Path | ConvertTo-SecureString)).Password
}

# ------------------------------------------------------------------ Key Vault (REST, en memoria)

function Get-KeyVaultAccessToken {
    $token = az account get-access-token --resource 'https://vault.azure.net' --query accessToken -o tsv
    Assert-Az 'obtener token de Key Vault'
    $token
}

function Test-KeyVaultSecret([string] $VaultName, [string] $SecretName) {
    $headers = @{ Authorization = "Bearer $(Get-KeyVaultAccessToken)" }
    try {
        $null = Invoke-RestMethod -Method Get -Headers $headers `
            -Uri "https://$VaultName.vault.azure.net/secrets/$SecretName`?api-version=7.4"
        return $true
    } catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return $false }
        throw "No se pudo consultar el secreto '$SecretName' (sin detalle para no exponer datos)"
    }
}

function Get-KeyVaultSecretValue([string] $VaultName, [string] $SecretName) {
    $headers = @{ Authorization = "Bearer $(Get-KeyVaultAccessToken)" }
    $response = Invoke-RestMethod -Method Get -Headers $headers `
        -Uri "https://$VaultName.vault.azure.net/secrets/$SecretName`?api-version=7.4"
    $response.value
}

function Set-KeyVaultSecretValue([string] $VaultName, [string] $SecretName, [string] $Value, [string] $ContentType = 'text/plain') {
    $headers = @{ Authorization = "Bearer $(Get-KeyVaultAccessToken)" }
    $body = @{ value = $Value; contentType = $ContentType } | ConvertTo-Json -Compress
    $null = Invoke-RestMethod -Method Put -Headers $headers -ContentType 'application/json' -Body $body `
        -Uri "https://$VaultName.vault.azure.net/secrets/$SecretName`?api-version=7.4"
}

function Grant-KeyVaultSecretsOfficer([string] $VaultName, [string] $ResourceGroup) {
    # El vault usa RBAC: para escribir secretos, quien ejecuta necesita Key Vault Secrets Officer.
    $vaultId = az keyvault show --name $VaultName --resource-group $ResourceGroup --query id -o tsv
    Assert-Az "leer el Key Vault $VaultName (¿ya se aplicó Terraform?)"
    $me = az ad signed-in-user show --query id -o tsv
    Assert-Az 'obtener el usuario actual'
    $role = 'Key Vault Secrets Officer'
    $assigned = az role assignment list --assignee $me --scope $vaultId --role $role --query '[].id' -o tsv
    if (-not $assigned) {
        az role assignment create --assignee-object-id $me --assignee-principal-type User --role $role --scope $vaultId -o none
        Assert-Az "asignar $role"
        Write-Host "Asignado '$role' al usuario actual sobre $VaultName."
    }
    # Esperar la propagación: listar secretos requiere el permiso.
    $headers = @{ Authorization = "Bearer $(Get-KeyVaultAccessToken)" }
    for ($i = 1; $i -le 30; $i++) {
        try {
            $null = Invoke-RestMethod -Method Get -Headers $headers -Uri "https://$VaultName.vault.azure.net/secrets?api-version=7.4&maxresults=1"
            return
        } catch {
            Write-Host "  esperando propagación del rol en Key Vault ($i/30)..."
            Start-Sleep -Seconds 10
        }
    }
    throw 'El permiso sobre Key Vault no se propagó en 5 minutos'
}

# ------------------------------------------------------------------ PostgreSQL

function Get-ConnectionStringPart([string] $ConnectionString, [string] $Key) {
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    # .psbase: sin él, PowerShell trata la asignación como una clave del diccionario.
    $builder.psbase.ConnectionString = $ConnectionString
    if ($builder.ContainsKey($Key)) { "$($builder[$Key])" } else { $null }
}

function Assert-V2ConnectionString([string] $ConnectionString, [string] $ExpectedDatabase) {
    $database = Get-ConnectionStringPart $ConnectionString 'database'
    if ($database -eq $script:V1DatabaseName) { throw 'ABORTADO: la connection string apunta a la base V1' }
    if ($database -ne $ExpectedDatabase) { throw "ABORTADO: la connection string apunta a '$database', se esperaba '$ExpectedDatabase'" }
}

function Invoke-PsqlStdin {
    param(
        [Parameter(Mandatory)] [string] $HostName,
        [Parameter(Mandatory)] [string] $Database,
        [Parameter(Mandatory)] [string] $User,
        [Parameter(Mandatory)] [string] $Password,
        [Parameter(Mandatory)] [string] $Sql
    )
    if ($Database -eq $script:V1DatabaseName) { throw 'ABORTADO: nunca se ejecuta SQL contra la base V1' }
    $env:PGPASSWORD = $Password
    $env:PGCONNECT_TIMEOUT = '20'
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = $Sql | & $script:Psql -d "host=$HostName port=5432 dbname=$Database user=$User sslmode=require" `
            -X -q -v ON_ERROR_STOP=1 -A -t -f - 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
        $env:PGPASSWORD = $null
    }
    # Nunca devolver literales ni verificadores: podrían estar en un mensaje de error.
    $clean = @($output | ForEach-Object { ("$_" -replace "'[^']*'", "'***'") -replace 'SCRAM-SHA-256\$\S+', '***' } |
        Where-Object { $_ -ne '' })
    [pscustomobject]@{ ExitCode = $code; Output = $clean }
}

function Get-CurrentPublicIp {
    $ip = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30).Trim()
    if ($ip -notmatch '^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$') { throw 'No se pudo determinar la IP pública' }
    $ip
}

function Invoke-WithTemporaryFirewallRule {
    <#
        Abre el servidor sólo para la IP pública actual, ejecuta el bloque y cierra la regla
        siempre. Verifica que el firewall quede exactamente como estaba.
    #>
    param(
        [Parameter(Mandatory)] [string] $ServerName,
        [Parameter(Mandatory)] [string] $ResourceGroup,
        [Parameter(Mandatory)] [scriptblock] $Action
    )

    $serverId = az postgres flexible-server show --name $ServerName --resource-group $ResourceGroup --query id -o tsv
    Assert-Az "leer el servidor $ServerName"
    $locks = az lock list --resource $serverId --query '[].name' -o tsv
    if ($locks) {
        throw "El servidor tiene locks ($locks): la regla temporal no podría eliminarse. Se esperaba correr esto antes de lock-postgres.ps1."
    }

    $snapshot = {
        (az postgres flexible-server firewall-rule list --resource-group $ResourceGroup --server-name $ServerName -o json | ConvertFrom-Json) |
            ForEach-Object { "$($_.name)|$($_.startIpAddress)|$($_.endIpAddress)" } | Sort-Object
    }
    $before = @(& $snapshot)
    $ip = Get-CurrentPublicIp
    $ruleName = 'tmp-turisclick-v2-' + (Get-Date -Format 'yyyyMMddHHmmss')
    $created = $false

    try {
        az postgres flexible-server firewall-rule create --resource-group $ResourceGroup --server-name $ServerName `
            --name $ruleName --start-ip-address $ip --end-ip-address $ip -o none
        Assert-Az 'crear regla temporal de firewall'
        $created = $true

        $added = @(@(& $snapshot) | Where-Object { $before -notcontains $_ })
        if ($added.Count -ne 1 -or $added[0] -ne "$ruleName|$ip|$ip") {
            throw 'ABORTADO: el firewall cambió de forma distinta a la esperada'
        }
        Write-Host "Regla temporal $ruleName abierta sólo para la IP actual."

        & $Action
    } finally {
        if ($created) {
            az postgres flexible-server firewall-rule delete --resource-group $ResourceGroup --server-name $ServerName `
                --name $ruleName --yes -o none
            $after = @(& $snapshot)
            if (($after -join ';') -ne ($before -join ';')) {
                Write-Host "INCIDENTE: el firewall NO quedó como estaba. Revisar la regla $ruleName de inmediato." -ForegroundColor Red
            } else {
                Write-Host 'Regla temporal eliminada: firewall idéntico al estado previo.'
            }
        }
    }
}
