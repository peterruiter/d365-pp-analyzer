<#
.SYNOPSIS
    Lets the deployed containers read and write the database.

.DESCRIPTION
    The API and the worker authenticate to Azure SQL as a managed identity. Azure SQL does
    not know about that identity until somebody creates a contained database user for it,
    and nothing in Bicep can do it: creating a database user is a statement run inside the
    database, not an ARM resource.

    So this runs once after the infrastructure is deployed, and again if the identity is
    ever recreated. It is safe to run repeatedly.

    The containers get read and write, and nothing else. Schema changes belong to
    Initialize-Database.ps1, which a person runs as themselves. An application that can
    alter its own schema is one that can do it by accident at three in the morning.

    You must be signed in as a member of the server's Entra administrator, because that is
    the only account that may add users. Run "az login" first if you have not.

.PARAMETER SqlServer
    Fully qualified server name, for example ppadev-sql.database.windows.net. Defaults to
    the ANALYZER_SQL_SERVER environment variable.

.PARAMETER Database
    Database name. Defaults to ANALYZER_SQL_DATABASE, then to the server name with -db.

.PARAMETER IdentityName
    Name of the managed identity, which is what Deploy-Infrastructure.ps1 prints as
    identityName. Azure SQL resolves it against Entra, so it must match exactly.

.EXAMPLE
    ./build/Grant-DatabaseAccess.ps1 -SqlServer ppadev-sql.database.windows.net -Database ppadev-db -IdentityName ppadev-identity

    Creates the user and grants it read and write.

.EXAMPLE
    ./build/Grant-DatabaseAccess.ps1 -SqlServer ppadev-sql.database.windows.net -Database ppadev-db -IdentityName ppadev-identity -WhatIf

    Prints the statements without running them.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SqlServer = $env:ANALYZER_SQL_SERVER,
    [string] $Database = $env:ANALYZER_SQL_DATABASE,
    [Parameter(Mandatory)][string] $IdentityName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Common.psm1') -Force

if (-not $SqlServer)
{
    throw 'No server. Pass -SqlServer, or set ANALYZER_SQL_SERVER. Deploy-Infrastructure.ps1 prints it.'
}

if (-not $Database)
{
    # The convention the Bicep uses: prefix-sql and prefix-db from the same prefix.
    $Database = ($SqlServer -split '\.')[0] -replace '-sql$', '-db'
}

# Bracket-quoted, and a name containing a closing bracket would break out of it. Identity
# names cannot contain one, but checking costs nothing and the alternative is a script that
# builds SQL from an unvalidated string.
if ($IdentityName -match '[\[\]]')
{
    throw "The identity name '$IdentityName' contains a bracket, which cannot be quoted safely here."
}

$sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$IdentityName')
BEGIN
    CREATE USER [$IdentityName] FROM EXTERNAL PROVIDER;
    PRINT 'User created.';
END
ELSE
    PRINT 'User already exists.';

-- Read and write, and nothing else. The containers never change the schema: that is
-- Initialize-Database.ps1, run by a person as themselves.
ALTER ROLE db_datareader ADD MEMBER [$IdentityName];
ALTER ROLE db_datawriter ADD MEMBER [$IdentityName];
PRINT 'Roles granted.';
"@

Write-Host "Server   : $SqlServer"
Write-Host "Database : $Database"
Write-Host "Identity : $IdentityName"
Write-Host ''

if (-not $PSCmdlet.ShouldProcess("$Database on $SqlServer", "Grant read and write to $IdentityName"))
{
    Write-Host 'Nothing was run. The statements would have been:'
    Write-Host ''
    Write-Host $sql
    return
}

Assert-Command -Name sqlcmd -InstallHint 'Install from https://aka.ms/sqlcmd' | Out-Null

$file = New-TemporaryFile
try
{
    Set-Content -Path $file -Value $sql -Encoding UTF8

    # -G is Entra authentication, which picks up whoever is signed in to the Azure CLI.
    # There is no password here because the server was deployed with SQL authentication
    # turned off.
    sqlcmd -S $SqlServer -d $Database -G -i $file -b

    if ($LASTEXITCODE -ne 0)
    {
        Write-Host ''
        Write-Host 'Failed. The usual cause is that you are not the Entra administrator of the server.'
        Write-Host "    Check who is: az sql server ad-admin list --resource-group <group> --server $(($SqlServer -split '\.')[0])"
        throw 'Grant failed.'
    }
}
finally
{
    Remove-Item $file -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "$IdentityName can now read and write $Database."
Write-Host ''
Write-Host '    Next: ./build/Publish-Container.ps1 to build and deploy the image.'
