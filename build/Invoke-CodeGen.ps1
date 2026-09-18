<#
.SYNOPSIS
    Regenerates every file that is produced from a contract.

.DESCRIPTION
    Contracts in build/contracts are the source of truth for the component taxonomy, the
    rule catalogue, the estimate model, the stages and the languages. This script turns
    them into C#.

    Run it after changing any contract, and before building if you are not sure whether
    somebody else changed one. It is fast and it is idempotent, so running it when it was
    not needed costs nothing.

    Nothing it writes is committed. Generated files end .g.cs, are gitignored, and are
    recreated from the contracts every time. Editing one is wasted work.

.PARAMETER SkipBuild
    Do not build afterwards. Useful when you are iterating on a generator rather than on
    the code it produces.

.EXAMPLE
    ./build/Invoke-CodeGen.ps1

    Regenerates everything and builds.

.EXAMPLE
    ./build/Invoke-CodeGen.ps1 -WhatIf

    Lists the files that would be written, without writing any of them.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Common.psm1') -Force
$root = Get-RepositoryRoot

$generators = @(
    'Generate-ComponentModel.ps1'
    'Generate-RuleCatalogue.ps1'
    'Generate-EstimateModel.ps1'
    'Generate-AcceptanceCriteria.ps1'
    'Generate-Locales.ps1'
)

Write-Host 'Checking contracts before generating anything.'
& (Join-Path $PSScriptRoot 'Test-Contracts.ps1')
Write-Host ''

# Into a throwaway folder first. A generator that emits C# which will not compile should not
# leave that C# in the repository, where the next person to open the solution finds a broken
# build with no idea which generator caused it.
Write-Host 'Checking what the generators emit.'
& (Join-Path $PSScriptRoot 'Test-Generators.ps1')
Write-Host ''

Write-Host 'Regenerating from build/contracts.'
Write-Host ''

foreach ($generator in $generators)
{
    $path = Join-Path $PSScriptRoot 'generators' $generator
    if (-not (Test-Path $path))
    {
        throw "Generator '$generator' is listed in Invoke-CodeGen.ps1 but does not exist at $path."
    }

    Write-Host "  $generator"
    & $path -WhatIf:$WhatIfPreference
    Write-Host ''
}

if ($SkipBuild)
{
    Write-Host 'Generation complete. Skipping the build because -SkipBuild was given.'
    Write-Host 'Next: dotnet build'
    return
}

if ($PSCmdlet.ShouldProcess('the solution', 'dotnet build'))
{
    Write-Host 'Building.'
    Push-Location $root
    try
    {
        dotnet build --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "The build failed after generation. The generator produced C# that does not compile, which is a bug in the generator rather than in the contract." }
    }
    finally
    {
        Pop-Location
    }

    Write-Host ''
    Write-Host 'Done. Generation and build both clean.'
    Write-Host 'Next: dotnet test'
}
