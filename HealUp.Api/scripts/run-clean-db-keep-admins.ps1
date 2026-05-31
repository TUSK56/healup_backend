# Runs clean-db-keep-admins.sql against the database from appsettings.Development.json.
# Requires sqlcmd (SQL Server command-line tools).

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlFile = Join-Path $scriptDir 'clean-db-keep-admins.sql'
$configPath = Join-Path $scriptDir '..\appsettings.Development.json'

if (-not (Test-Path $configPath)) {
    throw "Missing $configPath — copy appsettings.Development.example.json and set DefaultConnection."
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$connectionString = $config.ConnectionStrings.DefaultConnection
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw 'ConnectionStrings:DefaultConnection is empty.'
}

function Get-ConnectionPart {
    param([string]$Key)
    foreach ($part in ($connectionString -split ';')) {
        if ($part -match "^\s*$Key\s*=\s*(.+)\s*$") {
            return $Matches[1].Trim()
        }
    }
    return $null
}

$server = Get-ConnectionPart -Key 'Server'
if (-not $server) { $server = Get-ConnectionPart -Key 'Data Source' }
$database = Get-ConnectionPart -Key 'Database'
if (-not $database) { $database = Get-ConnectionPart -Key 'Initial Catalog' }
$user = Get-ConnectionPart -Key 'User Id'
if (-not $user) { $user = Get-ConnectionPart -Key 'User ID' }
$password = Get-ConnectionPart -Key 'Password'
if (-not $password) { $password = Get-ConnectionPart -Key 'Pwd' }

if (-not $server -or -not $database) {
    throw 'Could not parse Server and Database from DefaultConnection.'
}

Write-Host "Target database: $database on $server"
Write-Host "SQL script:      $sqlFile"
Write-Host ''
Write-Host 'This will DELETE all data except [admins]. Type YES to continue:' -ForegroundColor Yellow
$confirm = Read-Host
if ($confirm -ne 'YES') {
    Write-Host 'Aborted.'
    exit 0
}

$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if (-not $sqlcmd) {
    throw 'sqlcmd not found. Install SQL Server command-line tools or run clean-db-keep-admins.sql manually in SSMS / MonsterASP Run T-SQL.'
}

$args = @('-S', $server, '-d', $database, '-i', $sqlFile, '-b')
if ($user -and $password) {
    $args += @('-U', $user, '-P', $password)
}
else {
    $args += '-E'
}

& sqlcmd @args
if ($LASTEXITCODE -ne 0) {
    throw "sqlcmd failed with exit code $LASTEXITCODE"
}

Write-Host ''
Write-Host 'Database cleanup finished.' -ForegroundColor Green
