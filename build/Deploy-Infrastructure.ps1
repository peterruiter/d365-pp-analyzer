<#
.SYNOPSIS
    Creates the Azure resources this product runs on.

.DESCRIPTION
    Deploys infra/main.bicep into a resource group, creating it when absent.

    Written for somebody who has never used Azure. If you have an Azure subscription and
    the Azure CLI installed, everything below is copy and paste.

    What it creates, and roughly what it costs:

      Azure SQL, serverless      Pauses itself after an hour of inactivity and bills
                                 storage only while paused. For a tool used in bursts
                                 that is a few pounds a month rather than a few tens.
                                 Cold start after a pause is about a minute.

    There is no SQL administrator password anywhere, because the server is configured for
    Entra authentication only. You sign in as yourself. Nothing to store, nothing to
    rotate, nothing to leak.

    Safe to run more than once. Bicep deployments are declarative, so running it again
    brings the resources back to what the template says rather than creating a second set.

.PARAMETER NamePrefix
    Prefix for every resource name. Lowercase letters and numbers, three to twenty
    characters. Resources become <prefix>-sql and <prefix>-db, so pick something that
    identifies the engagement.

.PARAMETER ResourceGroup
    Resource group to deploy into. Created when it does not exist.

.PARAMETER Location
    Azure region, for example westeurope. Put client configuration in the region the
    client expects it to be in.

.PARAMETER SubscriptionId
    Subscription to deploy into. Defaults to whichever the CLI is currently set to, and
    the script prints which that is before it does anything.

.PARAMETER AzureAdClientSecret
    The Entra client secret the sign in flow uses.

    Pass it every single time. The template's default is empty and an omitted parameter takes
    the default, so a deployment that does not carry the secret removes it, and the first sign
    in after that fails for everybody with an error that reads like an outage. There is no way
    for a template to preserve a value it was not given.

    If you do not have it to hand, create a new one on the application registration and pass
    that instead. Rotating a secret costs a deployment; discovering at nine on a Monday that
    nobody can sign in costs considerably more.

.PARAMETER AzureAdClientSecretExpiresUtc
    The date the Entra client secret expires, for example 2027-09-16. Not a secret: it is
    what the system health page counts down to.

    Worth passing every time a secret is created or rotated. A client secret does not
    degrade, it works perfectly until a date and then every sign in fails at once, for
    everybody, with an error that reads like an outage. Without this date nothing can warn
    anybody, and the health page says so rather than staying quiet.

.PARAMETER SyncfusionLicenseKey
    Licence key for the PDF renderer, from the Syncfusion account portal under Downloads
    and Keys. Optional: without it the PDF report is not offered and the inventory
    workbook and the cutover runbook are unaffected.

    The key must cover document processing. One issued for the UI components alone is
    accepted at startup and then stamps a trial banner across every page, so the renderer
    checks and refuses rather than producing a watermarked document. The system health
    page says which of the three states a deployment is in.

    The key is version locked to the Syncfusion packages pinned in
    Directory.Packages.props. A major upgrade means asking for a new key.

.PARAMETER SkipFirewall
    Do not add a firewall rule for this machine's public IP. Use when deploying from a
    build agent that will never connect to the database itself.

.EXAMPLE
    ./build/Deploy-Infrastructure.ps1 -NamePrefix ppa -ResourceGroup rg-ppa -Location westeurope

    Creates the resource group and the database, and prints the connection string.

.EXAMPLE
    ./build/Deploy-Infrastructure.ps1 -NamePrefix ppa -ResourceGroup rg-ppa -Location westeurope -WhatIf

    Shows what would be created without creating anything.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9]{3,20}$')]
    [string] $NamePrefix,

    [Parameter(Mandatory)]
    [string] $ResourceGroup,

    [Parameter(Mandatory)]
    [string] $Location,

    [string] $SubscriptionId,

    [string] $AzureAdClientSecret,

    [string] $AzureAdClientSecretExpiresUtc,

    [string] $SyncfusionLicenseKey,

    [switch] $SkipFirewall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Common.psm1') -Force
$root = Get-RepositoryRoot

# ------------------------------------------------------------------ checks --
if (-not (Get-Command az -ErrorAction SilentlyContinue))
{
    throw 'The Azure CLI is not installed or not on PATH. Install it from https://aka.ms/azure-cli and run "az login".'
}

$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account)
{
    throw 'Not signed in to Azure. Run "az login" and try again.'
}

if ($SubscriptionId)
{
    az account set --subscription $SubscriptionId | Out-Null
    $account = az account show --output json | ConvertFrom-Json
}

Write-Host "Subscription : $($account.name)  ($($account.id))"
Write-Host "Signed in as : $($account.user.name)"
Write-Host "Resource group: $ResourceGroup"
Write-Host "Location      : $Location"
Write-Host "Name prefix   : $NamePrefix"
Write-Host ''

# The signed-in principal becomes the database administrator. Doing it this way means
# there is no password to invent, and the person who deployed it can immediately connect.
$signedIn = az ad signed-in-user show --output json 2>$null | ConvertFrom-Json
if (-not $signedIn)
{
    throw 'Could not read the signed-in user from Entra. The account needs permission to read its own profile.'
}

$clientIp = ''
if (-not $SkipFirewall)
{
    try
    {
        # Azure SQL refuses connections from an IP with no firewall rule, and the error
        # says so clearly enough that the rule is worth adding up front rather than after
        # somebody has read it.
        $clientIp = (Invoke-RestMethod -Uri 'https://api.ipify.org?format=json' -TimeoutSec 10).ip
        Write-Host "This machine's public IP: $clientIp  (a firewall rule will allow it)"
    }
    catch
    {
        Write-Warning 'Could not determine this machine''s public IP. No firewall rule will be added, so applying migrations from here will fail until one exists.'
    }
}

Write-Host ''

if (-not $PSCmdlet.ShouldProcess("$ResourceGroup in $($account.name)", 'Create the resource group and deploy infra/main.bicep'))
{
    Write-Host 'Nothing was created. Remove -WhatIf to deploy.'
    return
}

# ------------------------------------------------------------------ deploy --
Write-Host 'Creating the resource group if it does not exist.'
az group create --name $ResourceGroup --location $Location --output none

Write-Host 'Deploying. A first deployment of a SQL server takes a few minutes.'

$deploymentName = "ppa-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$template = Join-Path $root 'infra/main.bicep'

$parameters = @(
    "namePrefix=$NamePrefix"
    "location=$Location"
    "administratorObjectId=$($signedIn.id)"
    "administratorName=$($signedIn.userPrincipalName)"
    'administratorType=User'
    "clientIpAddress=$clientIp"
)

# Only when supplied, and the branch below warns about what that means. The template's
# default is an empty string, so omitting this does not leave an existing key alone, it
# removes it. Warning is better than silently turning the PDF off.
if ($AzureAdClientSecret)
{
    $parameters += "azureAdClientSecret=$AzureAdClientSecret"
}
else
{
    # The loudest thing this script says, because it is the one omission that takes the
    # product down rather than degrading it. A template cannot preserve a value it was not
    # given: the parameter defaults to empty, empty removes the secret, and the next person to
    # sign in gets an error that looks nothing like an expired credential.
    Write-Warning 'No Entra client secret passed. This deployment will REMOVE the one the'
    Write-Warning 'environment is using, and every sign in will fail immediately afterwards.'
    Write-Warning 'Pass -AzureAdClientSecret, or create a new secret and pass that.'
    Write-Host ''

    if (-not $PSCmdlet.ShouldContinue(
        'Continue and remove the Entra client secret?', 'Sign in will break'))
    {
        throw 'Stopped. Re-run with -AzureAdClientSecret.'
    }
}

if ($AzureAdClientSecretExpiresUtc)
{
    $parameters += "azureAdClientSecretExpiresUtc=$AzureAdClientSecretExpiresUtc"
}

if ($SyncfusionLicenseKey)
{
    $parameters += "syncfusionLicenseKey=$SyncfusionLicenseKey"
}
else
{
    # Said out loud because the template's default is an empty string, so leaving the
    # parameter off does not preserve a key that is already deployed, it clears it. The first
    # sign of that would be the PDF quietly disappearing from the reports list.
    Write-Warning 'No Syncfusion key passed, so the PDF report will be off after this deployment.'
    Write-Warning 'That includes clearing a key this environment already had. Pass -SyncfusionLicenseKey'
    Write-Warning 'to keep it. The inventory workbook and the cutover runbook are unaffected.'
    Write-Host ''
}

az deployment group create `
    --name $deploymentName `
    --resource-group $ResourceGroup `
    --template-file $template `
    --parameters @parameters `
    --output none

if ($LASTEXITCODE -ne 0)
{
    throw "The deployment failed. Run: az deployment group show --name $deploymentName --resource-group $ResourceGroup"
}

$outputs = az deployment group show --name $deploymentName --resource-group $ResourceGroup --query properties.outputs --output json | ConvertFrom-Json

Write-Host ''
Write-Host 'Done.'
Write-Host ''
Write-Host "SQL server   : $($outputs.sqlServer.value)"
Write-Host "Database     : $($outputs.sqlDatabase.value)"
Write-Host ''
Write-Host 'Connection string, which contains no secret because authentication is Entra:'
Write-Host ''
Write-Host "  $($outputs.sqlConnectionString.value)"
Write-Host ''
Write-Host 'Next, create the tables:'
Write-Host ''
Write-Host "  `$env:MIGRATOR_SQL_CONNECTION = '$($outputs.sqlConnectionString.value)'"
Write-Host '  ./build/Initialize-Database.ps1'
