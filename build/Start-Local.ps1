<#
.SYNOPSIS
    Runs the product on this machine, signed in, for looking at and photographing.

.DESCRIPTION
    There is no login bypass here and there does not need to be one. The product already
    signs you in locally: when no Entra application is configured it registers
    LocalSignInHandler instead, which is a scheme rather than an exemption. Every endpoint
    keeps its RequireAuthorization and the whole authorisation pipeline still runs; the only
    difference is where the identity came from. That matters, because a local run that
    skipped the checks would be exercising a different product from the deployed one, which
    is how a permissions bug ships.

    What was missing was not the capability but the instructions. This is them.

    The identity is whoever -Admin names, and it defaults to the person the deployment
    already has as its first global administrator. Using somebody who is already admitted
    means this writes no new row: it reads the real database as an existing administrator
    rather than inventing one. Point -Admin at anybody else and they will be seeded as a
    global administrator on first request, which is a real change to real data.

.PARAMETER Brand
    Which livery to wear. Defaults to whatever brands/ says is the default.

.PARAMETER Admin
    Who to sign in as. Must already be a global administrator, or they will be made one.

.PARAMETER Port
    Where to listen. Loopback only.

.PARAMETER Database
    Which database. Defaults to the deployed one, read as you, over Entra.

.EXAMPLE
    ./build/Start-Local.ps1
    Then open http://127.0.0.1:5199/app/ for the product, or / for the public site.

.EXAMPLE
    ./build/Start-Local.ps1 -Brand capgemini
    The same thing in the other livery, for comparing them.
#>
[CmdletBinding()]
param(
    [string] $Brand = 'powerpete',
    [string] $Admin = 'peter@powerpete.com',
    [int]    $Port = 5199,
    [string] $Database = 'Server=tcp:ppanalyzer-sql.database.windows.net,1433;Initial Catalog=ppanalyzer-db;Encrypt=True;Authentication=Active Directory Default;Connect Timeout=90;'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# The front end is served from wwwroot as files, so both have to have been built or the
# page is the last build somebody happened to leave there. This is the single most common
# way a local look at the product shows something that is not the product.
Write-Host 'Building the web app.' -ForegroundColor Cyan
Push-Location (Join-Path $root 'src/web')
try { & npm run build | Out-Null } finally { Pop-Location }

Write-Host 'Building the public site.' -ForegroundColor Cyan
& node (Join-Path $root 'src/microsite/build.mjs') `
    (Join-Path $root 'src/microsite') `
    (Join-Path $root 'src/PowerPete.Analyzer.Api/wwwroot') | Out-Null

# No AzureAd:* of any kind. Their absence is what selects the local sign-in scheme, so
# setting one here by accident would send you to a Microsoft sign-in page you cannot
# complete against localhost.
$env:ConnectionStrings__Analyzer   = $Database
$env:Access__InitialGlobalAdminUpn = $Admin
$env:LocalSignIn__DisplayName      = 'Peter Ruiter'
$env:ANALYZER_BRAND                = $Brand
$env:ASPNETCORE_ENVIRONMENT        = 'Development'
$env:ASPNETCORE_URLS               = "http://127.0.0.1:$Port"

Write-Host ''
Write-Host "Brand    : $Brand"
Write-Host "Signed in: $Admin"
Write-Host "Product  : http://127.0.0.1:$Port/app/"
Write-Host "Site     : http://127.0.0.1:$Port/"
Write-Host ''
Write-Host 'The browser caches index.html and the stylesheet. Add ?v=2 after a rebuild.' -ForegroundColor DarkGray
Write-Host ''

& dotnet run --project (Join-Path $root 'src/PowerPete.Analyzer.Api') --no-launch-profile
