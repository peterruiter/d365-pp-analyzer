<#
.SYNOPSIS
    Generates the component catalogue from the component model contract.

.DESCRIPTION
    Reads build/contracts/component-model.json and writes the component types, their craft
    and lifecycle classification, and the rule for computing the low code ratio.

    Generated rather than hand written because four things read this list and must not
    disagree: the extractor deciding what to ask for, the analyser deciding which rules
    apply, the scorer computing the ratio, and the screens showing the inventory.

    Called by build/Invoke-CodeGen.ps1.

.PARAMETER OutputPath
    Where the generated file goes. Defaults to the Domain project's Generated folder.

.EXAMPLE
    ./build/generators/Generate-ComponentModel.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param([string] $OutputPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..' 'Common.psm1') -Force

$root = Get-RepositoryRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'src/PowerPete.Analyzer.Domain/Generated' }

$contract = Get-Contract -Name 'component-model'
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

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("namespace $namespace;")
$lines.Add('')
$lines.Add('/// <summary>What a component is made of, and therefore who can change it.</summary>')
$lines.Add('public enum Craft')
$lines.Add('{')
foreach ($level in $contract.craftLevels.PSObject.Properties)
{
    $lines.Add("    /// <summary>$($level.Value)</summary>")
    $lines.Add("    $(ConvertTo-PascalCase $level.Name),")
}
$lines.Add('}')
$lines.Add('')
$lines.Add('/// <summary>How much life Microsoft has left in a component.</summary>')
$lines.Add('public enum Lifecycle')
$lines.Add('{')
foreach ($state in $contract.lifecycleStates.PSObject.Properties)
{
    $lines.Add("    /// <summary>$($state.Value)</summary>")
    $lines.Add("    $(ConvertTo-PascalCase $state.Name),")
}
$lines.Add('}')
$lines.Add('')
$lines.Add('/// <summary>Where a component can be seen from.</summary>')
$lines.Add('public enum EvidenceSource')
$lines.Add('{')
foreach ($source in $contract.evidenceSources.PSObject.Properties | Where-Object { $_.Name -ne 'description' })
{
    $lines.Add("    /// <summary>$($source.Value)</summary>")
    $lines.Add("    $(ConvertTo-PascalCase $source.Name),")
}
$lines.Add('}')
$lines.Add('')
$lines.Add('/// <summary>One kind of component this product understands.</summary>')
$lines.Add('/// <param name="Id">Stable identifier. Rules key on this.</param>')
$lines.Add('/// <param name="Name">What a consultant calls it.</param>')
$lines.Add('/// <param name="Domain">Which part of the platform it belongs to.</param>')
$lines.Add('/// <param name="Craft">What it is made of.</param>')
$lines.Add('/// <param name="Lifecycle">How much life it has left.</param>')
$lines.Add('/// <param name="Evidence">Which sources can see it.</param>')
$lines.Add('/// <param name="CountsTowardRatio">Whether it is counted into the low code ratio.</param>')
$lines.Add('/// <param name="Attributes">The attribute names an extraction is expected to fill.</param>')
$lines.Add('/// <param name="LifecycleNote">Why the lifecycle is what it is, where it is not current.</param>')
$lines.Add('public sealed record ComponentType(')
$lines.Add('    string Id,')
$lines.Add('    string Name,')
$lines.Add('    string Domain,')
$lines.Add('    Craft Craft,')
$lines.Add('    Lifecycle Lifecycle,')
$lines.Add('    IReadOnlyList<EvidenceSource> Evidence,')
$lines.Add('    bool CountsTowardRatio,')
$lines.Add('    IReadOnlyList<string> Attributes,')
$lines.Add('    string? LifecycleNote);')
$lines.Add('')
$lines.Add('/// <summary>Every component type, and the rule for the low code ratio.</summary>')
$lines.Add('/// <remarks>')
$lines.Add('/// The ratio excludes configuration deliberately. A solution with four hundred columns')
$lines.Add('/// and one plugin is not ninety-nine percent low code in any sense worth defending.')
$lines.Add('/// </remarks>')
$lines.Add('public static class ComponentCatalogue')
$lines.Add('{')
$lines.Add('    /// <summary>Every component type declared by the contract.</summary>')
$lines.Add('    public static IReadOnlyList<ComponentType> All { get; } =')
$lines.Add('    [')

foreach ($component in $contract.componentTypes)
{
    $evidence = if ($component.evidence) { '[' + (($component.evidence | ForEach-Object { "EvidenceSource.$(ConvertTo-PascalCase $_)" }) -join ', ') + ']' } else { '[]' }
    $attributes = if ($component.PSObject.Properties.Name -contains 'attributes') { QuoteList $component.attributes } else { '[]' }
    $note = if ($component.PSObject.Properties.Name -contains 'lifecycleNote') { Quote $component.lifecycleNote } elseif ($component.PSObject.Properties.Name -contains 'note') { Quote $component.note } else { 'null' }

    $lines.Add("        new($(Quote $component.id), $(Quote $component.name), $(Quote $component.domain), Craft.$(ConvertTo-PascalCase $component.craft), Lifecycle.$(ConvertTo-PascalCase $component.lifecycle), $evidence, $($component.countsTowardRatio.ToString().ToLowerInvariant()), $attributes, $note),")
}

$lines.Add('    ];')
$lines.Add('')
$lines.Add('    /// <summary>One component type by id, or null when it is not one.</summary>')
$lines.Add('    /// <param name="id">The identifier.</param>')
$lines.Add('    public static ComponentType? Find(string id) =>')
$lines.Add('        All.FirstOrDefault(type => type.Id.Equals(id, StringComparison.OrdinalIgnoreCase));')
$lines.Add('')
$lines.Add('    /// <summary>The types counted into the low code to high code ratio.</summary>')
$lines.Add('    public static IReadOnlyList<ComponentType> Counted { get; } =')
$lines.Add('        [.. All.Where(type => type.CountsTowardRatio)];')
$lines.Add('')
$lines.Add('    /// <summary>Everything Microsoft has stopped investing in, removed or deprecated.</summary>')
$lines.Add('    public static IReadOnlyList<ComponentType> Ageing { get; } =')
$lines.Add('        [.. All.Where(type => type.Lifecycle is Lifecycle.Dated or Lifecycle.Deprecated or Lifecycle.Removed)];')
$lines.Add('}')
$lines.Add('')

$output = Join-Path $OutputPath 'ComponentCatalogue.g.cs'
Write-GeneratedFile -Path $output -Content (($lines -join "`n")) -Contract 'component-model.json' -WhatIf:$WhatIfPreference
Write-Host "    $($contract.componentTypes.Count) component types -> ComponentCatalogue.g.cs"
