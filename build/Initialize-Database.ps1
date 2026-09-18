<#
.SYNOPSIS
    Creates or updates the database schema.

.DESCRIPTION
    Applies every migration in db/migrations, in order, to the database named by the
    connection string. Safe to run against a database that is already current: each
    migration creates what is absent and leaves what is present alone.

    Authentication is Entra. There is no password to supply, because the server was
    deployed with SQL authentication turned off. Whoever is signed in to the Azure CLI is
    who connects, so run "az login" first if you have not.

    Refuses to run when a migration file has changed since it was applied to this database.
    That is the rule about never editing an applied migration, enforced rather than written
    down: two databases that both claim to be up to date have to be the same shape.

.PARAMETER ConnectionString
    The database to build. Defaults to the MIGRATOR_SQL_CONNECTION environment variable,
    which Deploy-Infrastructure.ps1 prints when it finishes.

.EXAMPLE
    ./build/Initialize-Database.ps1 -ConnectionString 'Server=tcp:ppadev-sql.database.windows.net,1433;Initial Catalog=ppadev-db;Encrypt=True;'

    Creates the tables.

.EXAMPLE
    ./build/Initialize-Database.ps1 -WhatIf

    Lists the migrations that would be applied, without connecting to anything.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ConnectionString = $env:MIGRATOR_SQL_CONNECTION
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Common.psm1') -Force
$root = Get-RepositoryRoot

$migrations = Get-ChildItem -Path (Join-Path $root 'db/migrations') -Filter '*.sql' | Sort-Object Name

Write-Host "Migrations found: $($migrations.Count)"
foreach ($migration in $migrations)
{
    Write-Host "  $($migration.Name)"
}
Write-Host ''

if (-not $ConnectionString)
{
    throw 'No connection string. Pass -ConnectionString, or set MIGRATOR_SQL_CONNECTION. Deploy-Infrastructure.ps1 prints it when it finishes.'
}

# The catalog is named rather than the whole string, because the string is about to be
# echoed into a terminal and a connection string is the kind of thing people paste into
# chat messages without reading it first.
$catalog = if ($ConnectionString -match 'Initial Catalog=([^;]+)') { $Matches[1] } else { 'unknown' }
$server = if ($ConnectionString -match 'Server=tcp:([^,;]+)') { $Matches[1] } else { 'unknown' }

Write-Host "Server   : $server"
Write-Host "Database : $catalog"
Write-Host ''

if (-not $PSCmdlet.ShouldProcess("$catalog on $server", 'Apply migrations'))
{
    Write-Host 'Nothing was applied. Remove -WhatIf to run them.'
    return
}

$env:MIGRATOR_SQL_CONNECTION = $ConnectionString

try
{
    Push-Location $root
    try
    {
        dotnet run --project src/PowerPete.Analyzer.Jobs --no-launch-profile -- migrate-database
        $exitCode = $LASTEXITCODE
    }
    finally
    {
        Pop-Location
    }
}
finally
{
    $env:MIGRATOR_SQL_CONNECTION = $null
}

Write-Host ''
if ($exitCode -eq 0)
{
    Write-Host 'Schema is current.'
    Write-Host ''
    Write-Host 'Next: ./build/Test-DatabaseRoundTrip.ps1 to prove the tables behave as the code expects.'
}
else
{
    Write-Host 'Migrations failed. Nothing further will work until this is resolved.'
}

exit $exitCode
