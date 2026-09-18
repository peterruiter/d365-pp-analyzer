<#
.SYNOPSIS
    Generates the estimate bands, the complexity rules and the per engagement fixed costs.

.DESCRIPTION
    Reads build/contracts/estimate-model.json and the complexity rules from
    build/contracts/component-model.json, and writes them as C#.

    Generated because three things read them and must not disagree: the estimator falling back
    to a band, the rater producing the customisation chart, and the scorer adding the fixed
    costs. Before this existed the command line carried its own copy of the band table, which
    is exactly the kind of duplicate that drifts quietly and then produces two different
    numbers for the same estate.

    Called by build/Invoke-CodeGen.ps1.

.PARAMETER OutputPath
    Where the generated file goes. Defaults to the Domain project's Generated folder.

.EXAMPLE
    ./build/generators/Generate-EstimateModel.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param([string] $OutputPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..' 'Common.psm1') -Force

$root = Get-RepositoryRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'src/PowerPete.Analyzer.Domain/Generated' }

$estimates = Get-Contract -Name 'estimate-model'
$components = Get-Contract -Name 'component-model'
$namespace = 'PowerPete.Analyzer.Domain'

function Quote($value)
{
    if ($null -eq $value) { return 'null' }
    return '"' + ($value -replace '\\', '\\' -replace '"', '\"' -replace "`r", '' -replace "`n", ' ') + '"'
}

function Decimal($value) { return ([decimal]$value).ToString([System.Globalization.CultureInfo]::InvariantCulture) + 'm' }

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("namespace $namespace;")
$lines.Add('')
$lines.Add('/// <summary>One per engagement cost, added once and shown separately.</summary>')
$lines.Add('/// <param name="Id">Its identifier.</param>')
$lines.Add('/// <param name="Name">What it is called in the report.</param>')
$lines.Add('/// <param name="Low">Lower bound in hours.</param>')
$lines.Add('/// <param name="High">Upper bound in hours.</param>')
$lines.Add('/// <param name="Note">Why it is what it is.</param>')
$lines.Add('public sealed record EngagementCost(string Id, string Name, decimal Low, decimal High, string? Note);')
$lines.Add('')
$lines.Add('/// <summary>The bands, the fixed costs and the complexity rules, from the contracts.</summary>')
$lines.Add('/// <remarks>')
$lines.Add('/// Generated. Anything here that also exists as a literal somewhere in the code is a bug')
$lines.Add('/// waiting to produce two different numbers for the same estate.')
$lines.Add('/// </remarks>')
$lines.Add('public static class EstimateCatalogue')
$lines.Add('{')
$lines.Add('    /// <summary>The fallback estimate for each rule band.</summary>')
$lines.Add('    public static IReadOnlyDictionary<string, EstimateBand> Bands { get; } =')
$lines.Add('        new Dictionary<string, EstimateBand>(StringComparer.Ordinal)')
$lines.Add('        {')

foreach ($band in $estimates.bands.PSObject.Properties | Where-Object { $_.Name -ne 'description' })
{
    $value = $band.Value
    $lines.Add("            [$(Quote $band.Name)] = new($(Quote $band.Name), $(Decimal $value.low), $(Decimal $value.high), $(Quote $value.rationale)),")
}

$lines.Add('        };')
$lines.Add('')
$lines.Add('    /// <summary>Costs that exist once per engagement rather than once per finding.</summary>')
$lines.Add('    /// <remarks>')
$lines.Add('    /// Never distributed across findings to make individual numbers look bigger. They are')
$lines.Add('    /// named in the report so a client can see them.')
$lines.Add('    /// </remarks>')
$lines.Add('    public static IReadOnlyList<EngagementCost> FixedCosts { get; } =')
$lines.Add('    [')

foreach ($cost in $estimates.fixedCosts.items)
{
    $note = if ($cost.PSObject.Properties.Name -contains 'note') { Quote $cost.note } else { 'null' }
    $lines.Add("        new($(Quote $cost.id), $(Quote $cost.name), $(Decimal $cost.low), $(Decimal $cost.high), $note),")
}

$lines.Add('    ];')
$lines.Add('')
$lines.Add('    /// <summary>The valid story point values.</summary>')
$lines.Add('    public static IReadOnlyList<int> PointScale { get; } = [' + ($estimates.storyPoints.scale -join ', ') + '];')
$lines.Add('')
$lines.Add('    /// <summary>How each component type is rated simple, medium or complex.</summary>')
$lines.Add('    /// <remarks>')
$lines.Add('    /// A type with no rule here is simple, because a table is a table. A type WITH a rule')
$lines.Add('    /// whose measure came back empty is unrated, which is a different statement.')
$lines.Add('    /// </remarks>')
$lines.Add('    public static IReadOnlyList<ComplexityRuleDefinition> ComplexityRules { get; } =')
$lines.Add('    [')

foreach ($rule in $components.complexityRules.rules)
{
    $measure = if ($null -eq $rule.measure) { 'null' } else { Quote $rule.measure }
    $bands = ($rule.bands | ForEach-Object {
        $upTo = if ($null -eq $_.upTo) { 'null' } else { "$($_.upTo)" }
        "new($upTo, $(Quote $_.level))"
    }) -join ', '

    $lines.Add("        new($(Quote $rule.componentType), $measure, [$bands]),")
}

$lines.Add('    ];')
$lines.Add('}')
$lines.Add('')
$lines.Add('/// <summary>One band of a complexity rule.</summary>')
$lines.Add('/// <param name="UpTo">Upper bound of the measured value, or null for the top band.</param>')
$lines.Add('/// <param name="Level">simple, medium, complex or unrated.</param>')
$lines.Add('public sealed record ComplexityBandDefinition(int? UpTo, string Level);')
$lines.Add('')
$lines.Add('/// <summary>One component type''s complexity rule.</summary>')
$lines.Add('/// <param name="ComponentTypeId">Which type.</param>')
$lines.Add('/// <param name="Measure">The attribute to read, or null when the rule is flat.</param>')
$lines.Add('/// <param name="Bands">The bands, in order.</param>')
$lines.Add('public sealed record ComplexityRuleDefinition(string ComponentTypeId, string? Measure, IReadOnlyList<ComplexityBandDefinition> Bands);')
$lines.Add('')

$output = Join-Path $OutputPath 'EstimateCatalogue.g.cs'
Write-GeneratedFile -Path $output -Content (($lines -join "`n")) -Contract 'estimate-model.json' -WhatIf:$WhatIfPreference
Write-Host "    $(@($estimates.bands.PSObject.Properties).Count - 1) bands, $(@($estimates.fixedCosts.items).Count) fixed costs, $(@($components.complexityRules.rules).Count) complexity rules -> EstimateCatalogue.g.cs"
