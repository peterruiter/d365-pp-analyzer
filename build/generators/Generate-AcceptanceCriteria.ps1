<#
.SYNOPSIS
    Generates the acceptance criteria from the contract.

.DESCRIPTION
    Reads build/contracts/acceptance-criteria.json and writes one entry per rule that produces
    a work item.

    Generated rather than loaded from disk at run time, so a container carries its own criteria
    and a deployment cannot separate the code from the text it publishes. There is deliberately
    no fallback anywhere: a rule with no criterion fails the contract check and the backlog
    builder throws, because a generated work item whose acceptance criterion is boilerplate
    teaches a team to close the rest without reading them.

    Called by build/Invoke-CodeGen.ps1.

.PARAMETER OutputPath
    Where the generated file goes. Defaults to the Domain project's Generated folder.

.EXAMPLE
    ./build/generators/Generate-AcceptanceCriteria.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param([string] $OutputPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..' 'Common.psm1') -Force

$root = Get-RepositoryRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'src/PowerPete.Analyzer.Domain/Generated' }

$contract = Get-Contract -Name 'acceptance-criteria'
$namespace = 'PowerPete.Analyzer.Domain'

function Quote($value)
{
    if ($null -eq $value) { return 'null' }
    return '"' + ($value -replace '\\', '\\' -replace '"', '\"' -replace "`r", '' -replace "`n", ' ') + '"'
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("namespace $namespace;")
$lines.Add('')
$lines.Add('/// <summary>What has to be true before a work item is closed, and how to prove it.</summary>')
$lines.Add('/// <param name="Given">The component, in context.</param>')
$lines.Add('/// <param name="When">What has been done.</param>')
$lines.Add('/// <param name="Then">What must then be true.</param>')
$lines.Add('/// <param name="TestRequirement">How to prove it, which is usually the larger half of the work.</param>')
$lines.Add('public sealed record AcceptanceCriterion(string Given, string When, string Then, string TestRequirement);')
$lines.Add('')
$lines.Add('/// <summary>One criterion per rule that produces a work item.</summary>')
$lines.Add('/// <remarks>')
$lines.Add('/// There is no generic fallback and there will not be one. A rule missing from here fails')
$lines.Add('/// the contract check before anything is generated.')
$lines.Add('/// </remarks>')
$lines.Add('public static class AcceptanceCriteriaCatalogue')
$lines.Add('{')
$lines.Add('    /// <summary>Every criterion, keyed by rule id.</summary>')
$lines.Add('    public static IReadOnlyDictionary<string, AcceptanceCriterion> All { get; } =')
$lines.Add('        new Dictionary<string, AcceptanceCriterion>(StringComparer.Ordinal)')
$lines.Add('        {')

foreach ($entry in $contract.criteria.PSObject.Properties)
{
    $value = $entry.Value
    $lines.Add("            [$(Quote $entry.Name)] = new($(Quote $value.given), $(Quote $value.when), $(Quote $value.then), $(Quote $value.testRequirement)),")
}

$lines.Add('        };')
$lines.Add('}')
$lines.Add('')

$output = Join-Path $OutputPath 'AcceptanceCriteriaCatalogue.g.cs'
Write-GeneratedFile -Path $output -Content (($lines -join "`n")) -Contract 'acceptance-criteria.json' -WhatIf:$WhatIfPreference
Write-Host "    $(@($contract.criteria.PSObject.Properties).Count) acceptance criteria -> AcceptanceCriteriaCatalogue.g.cs"
