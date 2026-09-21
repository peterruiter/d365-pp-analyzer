<#
.SYNOPSIS
    Generates the rule catalogue from the rule contract.

.DESCRIPTION
    Reads build/contracts/rule-catalogue.json and writes every rule the product can raise,
    with its severity, the component types it applies to, the evidence it needs and the
    estimate band it falls back to.

    The detection logic is not generated. Each rule has a handler written by hand and matched
    to the contract by id, and a rule in the contract with no handler fails a test. That is
    the split: the contract owns what is reported and why, the code owns how it is found.

    Called by build/Invoke-CodeGen.ps1.

.PARAMETER OutputPath
    Where the generated file goes. Defaults to the Domain project's Generated folder.

.EXAMPLE
    ./build/generators/Generate-RuleCatalogue.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param([string] $OutputPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..' 'Common.psm1') -Force

$root = Get-RepositoryRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'src/PowerPete.Analyzer.Domain/Generated' }

$contract = Get-Contract -Name 'rule-catalogue'
$namespace = 'PowerPete.Analyzer.Domain'

function Quote($value)
{
    if ($null -eq $value) { return 'null' }
    return '"' + ($value -replace '\\', '\\' -replace '"', '\"' -replace "`r", '' -replace "`n", ' ') + '"'
}

function QuoteList($values)
{
    if ($null -eq $values -or @($values).Count -eq 0) { return '[]' }
    return '[' + (($values | ForEach-Object { Quote $_ }) -join ', ') + ']'
}

function Member($object, $name)
{
    if ($object.PSObject.Properties.Name -contains $name) { return $object.$name }
    return $null
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("namespace $namespace;")
$lines.Add('')
$lines.Add('/// <summary>How much a finding matters, by consequence rather than by annoyance.</summary>')
$lines.Add('public enum Severity')
$lines.Add('{')
foreach ($severity in $contract.severities.PSObject.Properties)
{
    $lines.Add("    /// <summary>$($severity.Value)</summary>")
    $lines.Add("    $(ConvertTo-PascalCase $severity.Name),")
}
$lines.Add('}')
$lines.Add('')
$lines.Add('/// <summary>One kind of finding this product can raise.</summary>')
$lines.Add('/// <param name="Id">Stable identifier. The handler, the work item tag and the override table all key on this.</param>')
$lines.Add('/// <param name="Name">What it is called in a report.</param>')
$lines.Add('/// <param name="Category">Which part of the report it appears under.</param>')
$lines.Add('/// <param name="Severity">Consequence, not annoyance.</param>')
$lines.Add('/// <param name="AppliesTo">Component type ids. Empty means the rule is about the solution as a whole.</param>')
$lines.Add('/// <param name="Evidence">Evidence source names this rule needs. An extraction that reaches none of them reports the rule as not assessed.</param>')
$lines.Add('/// <param name="Why">Why it matters, in the sentence that goes into every work item for this rule.</param>')
$lines.Add('/// <param name="Recommendation">What to do about it.</param>')
$lines.Add('/// <param name="Modernisation">The modernisation entry offering the replacement options, where there is one.</param>')
$lines.Add('/// <param name="WorkItemType">What it becomes in a backlog.</param>')
$lines.Add('/// <param name="EstimateBand">The fallback estimate, used when there is no override and no model estimate.</param>')
$lines.Add('/// <param name="FalsePositive">Where this rule is known to be wrong, so a consultant reads it before quoting the finding.</param>')
$lines.Add('/// <param name="RoadmapRow">business or process.</param>')
$lines.Add('/// <param name="RoadmapColumn">architecture or technology.</param>')
$lines.Add('/// <param name="RoadmapBand">unclutter, accelerate or innovate.</param>')
$lines.Add('public sealed record AnalysisRule(')
$lines.Add('    string Id,')
$lines.Add('    string Name,')
$lines.Add('    string Category,')
$lines.Add('    Severity Severity,')
$lines.Add('    IReadOnlyList<string> AppliesTo,')
$lines.Add('    IReadOnlyList<string> Evidence,')
$lines.Add('    string Why,')
$lines.Add('    string Recommendation,')
$lines.Add('    string? Modernisation,')
$lines.Add('    string WorkItemType,')
$lines.Add('    string EstimateBand,')
$lines.Add('    string? FalsePositive,')
$lines.Add('    string RoadmapRow,')
$lines.Add('    string RoadmapColumn,')
$lines.Add('    string RoadmapBand);')
$lines.Add('')
$lines.Add('/// <summary>Every rule, and the categories they sort into.</summary>')
$lines.Add('/// <remarks>')
$lines.Add('/// Rules sourced from the Power Apps checker are not in here. They arrive at run time under')
$lines.Add('/// the checker''s own ids and are mapped onto these categories, because Microsoft maintains')
$lines.Add('/// them and a second copy would be worse and would go stale.')
$lines.Add('/// </remarks>')
$lines.Add('public static class RuleCatalogue')
$lines.Add('{')
$lines.Add('    /// <summary>Every rule declared by the contract.</summary>')
$lines.Add('    public static IReadOnlyList<AnalysisRule> All { get; } =')
$lines.Add('    [')

foreach ($rule in $contract.rules)
{
    $modernisation = Quote (Member $rule 'modernisation')
    $falsePositive = Quote (Member $rule 'falsePositive')

    $lines.Add("        new($(Quote $rule.id), $(Quote $rule.name), $(Quote $rule.category), Severity.$(ConvertTo-PascalCase $rule.severity), $(QuoteList $rule.appliesTo), $(QuoteList $rule.evidence), $(Quote $rule.why), $(Quote $rule.recommendation), $modernisation, $(Quote $rule.workItemType), $(Quote $rule.estimateBand), $falsePositive, $(Quote $rule.roadmap.row), $(Quote $rule.roadmap.column), $(Quote $rule.roadmap.band)),")
}

$lines.Add('    ];')
$lines.Add('')
$lines.Add('    /// <summary>One rule by id, or null.</summary>')
$lines.Add('    /// <param name="id">The identifier.</param>')
$lines.Add('    public static AnalysisRule? Find(string id) =>')
$lines.Add('        All.FirstOrDefault(rule => rule.Id.Equals(id, StringComparison.OrdinalIgnoreCase));')
$lines.Add('')
$lines.Add('    /// <summary>The rules that apply to one kind of component, plus the solution wide ones.</summary>')
$lines.Add('    /// <param name="componentTypeId">The component type.</param>')
$lines.Add('    public static IReadOnlyList<AnalysisRule> For(string componentTypeId) =>')
$lines.Add('        [.. All.Where(rule => rule.AppliesTo.Contains(componentTypeId, StringComparer.OrdinalIgnoreCase))];')
$lines.Add('')
$lines.Add('    /// <summary>The rules that produce work items. Informational rules do not.</summary>')
$lines.Add('    public static IReadOnlyList<AnalysisRule> Actionable { get; } =')
$lines.Add('        [.. All.Where(rule => !rule.WorkItemType.Equals("none", StringComparison.OrdinalIgnoreCase))];')
$lines.Add('')
$lines.Add('    /// <summary>Where each rule sits on the roadmap grid, keyed by rule id.</summary>')
$lines.Add('    /// <remarks>')
$lines.Add('    /// Every rule has one. A finding with nowhere to go disappears from the one slide a')
$lines.Add('    /// client keeps after the rest of the report is filed.')
$lines.Add('    /// </remarks>')
$lines.Add('    public static IReadOnlyDictionary<string, (string Row, string Column, string Band)> Roadmap { get; } =')
$lines.Add('        All.ToDictionary(rule => rule.Id, rule => (rule.RoadmapRow, rule.RoadmapColumn, rule.RoadmapBand), StringComparer.Ordinal);')
$lines.Add('')
$lines.Add('    /// <summary>What each category is called, keyed by its id.</summary>')
$lines.Add('    /// <remarks>')
$lines.Add('    /// The contract has carried these since it was written and nothing read them. The')
$lines.Add('    /// backlog made its epic titles by upper casing the first letter of the id instead,')
$lines.Add('    /// so a client''s board got an epic called "Ai" and another called "Alm" while the')
$lines.Add('    /// contract sat there saying "AI components" and "ALM and solution hygiene".')
$lines.Add('    ///')
$lines.Add('    /// English. It is the fallback a localiser uses when a language has no translation,')
$lines.Add('    /// which is better than an identifier in every language including this one.')
$lines.Add('    /// </remarks>')
$lines.Add('    public static IReadOnlyDictionary<string, string> CategoryNames { get; } =')
$lines.Add('        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)')
$lines.Add('        {')

foreach ($category in $contract.categories)
{
    $lines.Add(('            ["{0}"] = "{1}",' -f $category.id, ($category.name -replace '"', '\"')))
}

$lines.Add('        };')
$lines.Add('}')
$lines.Add('')

$output = Join-Path $OutputPath 'RuleCatalogue.g.cs'
Write-GeneratedFile -Path $output -Content (($lines -join "`n")) -Contract 'rule-catalogue.json' -WhatIf:$WhatIfPreference
Write-Host "    $($contract.rules.Count) rules -> RuleCatalogue.g.cs"
