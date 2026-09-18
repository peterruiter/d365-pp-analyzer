<#
.SYNOPSIS
    Checks the contracts refer to each other correctly, before a generator turns them into code.

.DESCRIPTION
    The contracts point at each other constantly. A rule names component types, a rule names
    a modernisation entry, a modernisation option names an effort band, a stage names a mode.
    Every one of those is a string that nobody spell checks.

    Broken references do not fail a build. They fail at run time as a rule that silently
    matches nothing, which looks exactly like an estate with no findings. That is the failure
    mode this whole product exists to avoid, so it is caught here instead.

    Errors stop generation. Warnings do not: they are places the contracts are incomplete
    rather than wrong, and the count is printed so it can be watched rather than ignored.

.EXAMPLE
    ./build/Test-Contracts.ps1

    Checks everything. Called automatically by Invoke-CodeGen.ps1.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Common.psm1') -Force

$errors = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

$components = Get-Contract -Name 'component-model'
$rules = Get-Contract -Name 'rule-catalogue'
$modernisation = Get-Contract -Name 'modernisation-map'
$estimates = Get-Contract -Name 'estimate-model'
$stages = Get-Contract -Name 'analysis-stages'
$sources = Get-Contract -Name 'extraction-sources'
$devops = Get-Contract -Name 'devops-mapping'

$componentIds = $components.componentTypes | ForEach-Object { $_.id }
$domainIds = $components.domains | ForEach-Object { $_.id }
$craftLevels = $components.craftLevels.PSObject.Properties.Name
$lifecycleStates = $components.lifecycleStates.PSObject.Properties.Name
$evidenceSources = $components.evidenceSources.PSObject.Properties.Name | Where-Object { $_ -ne 'description' }
$categoryIds = $rules.categories | ForEach-Object { $_.id }
$severityIds = $rules.severities.PSObject.Properties.Name
$modernisationIds = $modernisation.entries | ForEach-Object { $_.id }
$bandIds = $estimates.bands.PSObject.Properties.Name | Where-Object { $_ -ne 'description' }

# ------------------------------------------------------------- component model --
foreach ($component in $components.componentTypes)
{
    if ($component.domain -notin $domainIds) { $errors.Add("Component '$($component.id)' is in domain '$($component.domain)', which is not declared.") }
    if ($component.craft -notin $craftLevels) { $errors.Add("Component '$($component.id)' has craft '$($component.craft)', which is not a declared level.") }
    if ($component.lifecycle -notin $lifecycleStates) { $errors.Add("Component '$($component.id)' has lifecycle '$($component.lifecycle)', which is not a declared state.") }

    foreach ($source in $component.evidence)
    {
        if ($source -notin $evidenceSources) { $errors.Add("Component '$($component.id)' declares evidence source '$source', which is not declared.") }
    }

    # A lifecycle claim that is not 'current' is an assertion about Microsoft's roadmap, and
    # one with no verification state behind it is the kind of claim that ends up in a client
    # report and cannot be defended when somebody asks where it came from.
    if ($component.lifecycle -ne 'current' -and -not ($component.PSObject.Properties.Name -contains 'lifecycleVerification'))
    {
        $errors.Add("Component '$($component.id)' claims lifecycle '$($component.lifecycle)' with no lifecycleVerification. Every claim that something is dated, deprecated or removed has to say where that came from.")
    }
}

# --------------------------------------------------------------- rule catalogue --
foreach ($rule in $rules.rules)
{
    if ($rule.category -notin $categoryIds) { $errors.Add("Rule '$($rule.id)' is in category '$($rule.category)', which is not declared.") }
    if ($rule.severity -notin $severityIds) { $errors.Add("Rule '$($rule.id)' has severity '$($rule.severity)', which is not declared.") }

    foreach ($component in $rule.appliesTo)
    {
        if ($component -notin $componentIds) { $errors.Add("Rule '$($rule.id)' applies to component type '$component', which does not exist in component-model.json.") }
    }

    foreach ($source in $rule.evidence)
    {
        if ($source -notin $evidenceSources) { $errors.Add("Rule '$($rule.id)' needs evidence source '$source', which is not declared.") }
    }

    if ([string]::IsNullOrWhiteSpace($rule.detection))
    {
        $errors.Add("Rule '$($rule.id)' has no detection. A catalogue that lists aspirations reads identically to one that lists capabilities.")
    }

    if ($rule.estimateBand -notin $bandIds) { $errors.Add("Rule '$($rule.id)' uses estimate band '$($rule.estimateBand)', which is not declared in estimate-model.json.") }

    if (($rule.PSObject.Properties.Name -contains 'modernisation') -and $rule.modernisation -notin $modernisationIds)
    {
        $errors.Add("Rule '$($rule.id)' points at modernisation entry '$($rule.modernisation)', which does not exist.")
    }
}

# ----------------------------------------------------------- modernisation map --
foreach ($entry in $modernisation.entries)
{
    if ([string]::IsNullOrWhiteSpace($entry.leaveItAlone))
    {
        $errors.Add("Modernisation entry '$($entry.id)' has no case for leaving it alone. Every entry needs one: a report where every finding leads to work is a report a client stops believing.")
    }

    foreach ($option in $entry.options)
    {
        if ($option.band -notin $bandIds) { $errors.Add("Modernisation entry '$($entry.id)' offers an option with band '$($option.band)', which is not declared.") }
    }
}

# --------------------------------------------------------------- stages and modes --
$stageIds = $stages.stages | ForEach-Object { $_.id }
foreach ($mode in $stages.modes)
{
    if ($mode.stopsAfter -notin $stageIds) { $errors.Add("Run mode '$($mode.id)' stops after stage '$($mode.stopsAfter)', which does not exist.") }
}

$publish = $stages.stages | Where-Object { $_.id -eq 'publish' }
if ($publish.writes -ne 'target') { $errors.Add("The publish stage must declare that it writes to a target. It is the only stage in the product that writes anywhere outside its own database.") }

foreach ($stage in $stages.stages | Where-Object { $_.id -ne 'publish' })
{
    if ($stage.writes -eq 'target') { $errors.Add("Stage '$($stage.id)' declares a target write. Only publish may, and only behind an approval.") }
}

# -------------------------------------------------------------------- sources --
foreach ($mode in $sources.modes)
{
    foreach ($source in $evidenceSources)
    {
        if (-not ($mode.reaches.PSObject.Properties.Name -contains $source))
        {
            $errors.Add("Extraction mode '$($mode.id)' says nothing about evidence source '$source'. An unstated reach becomes a silently skipped rule.")
        }
    }
}

# ----------------------------------------------------- acceptance criteria cover --
# An error rather than a warning. There is no fallback criterion by design: a generated
# work item whose acceptance criterion is boilerplate teaches a team to close the rest
# without reading them, and a rule that reaches a publish without one would do exactly that.
$criteria = Get-Contract -Name 'acceptance-criteria'
$withCriteria = $criteria.criteria.PSObject.Properties.Name
$actionable = $rules.rules | Where-Object { $_.workItemType -ne 'none' }

foreach ($rule in $actionable)
{
    if ($rule.id -notin $withCriteria)
    {
        $errors.Add("Rule '$($rule.id)' produces a $($rule.workItemType) and has no acceptance criterion in acceptance-criteria.json.")
        continue
    }

    $criterion = $criteria.criteria.($rule.id)
    foreach ($field in @('given', 'when', 'then', 'testRequirement'))
    {
        if ([string]::IsNullOrWhiteSpace($criterion.$field))
        {
            $errors.Add("Acceptance criterion for '$($rule.id)' has an empty '$field'.")
        }
    }
}

foreach ($id in $withCriteria)
{
    if ($id -notin ($actionable | ForEach-Object { $_.id }))
    {
        $warnings.Add("Acceptance criterion '$id' does not match any rule that produces a work item. Either the rule was renamed or it stopped being actionable.")
    }
}


# --------------------------------------------------------------- roadmap --
# Every rule has to be placeable, including the informational ones: a finding with no position
# silently vanishes from the one slide a client keeps after the rest of the report is filed.
$rows = $rules.roadmap.axes.row.PSObject.Properties.Name
$columns = $rules.roadmap.axes.column.PSObject.Properties.Name
$bands = $rules.roadmap.bands.PSObject.Properties.Name

foreach ($rule in $rules.rules)
{
    if (-not ($rule.PSObject.Properties.Name -contains 'roadmap'))
    {
        $errors.Add("Rule '$($rule.id)' has no roadmap position. A finding with nowhere to go disappears from the roadmap without anything saying so.")
        continue
    }

    if ($rule.roadmap.row -notin $rows) { $errors.Add("Rule '$($rule.id)' sits on roadmap row '$($rule.roadmap.row)', which is not declared.") }
    if ($rule.roadmap.column -notin $columns) { $errors.Add("Rule '$($rule.id)' sits in roadmap column '$($rule.roadmap.column)', which is not declared.") }
    if ($rule.roadmap.band -notin $bands) { $errors.Add("Rule '$($rule.id)' is in roadmap band '$($rule.roadmap.band)', which is not declared.") }
}

# ------------------------------------------------------------ complexity --
$components = Get-Contract -Name 'component-model'
$complexityLevels = $components.complexityRules.levels.PSObject.Properties.Name

foreach ($rule in $components.complexityRules.rules)
{
    if ($rule.componentType -notin $componentIds)
    {
        $errors.Add("A complexity rule names component type '$($rule.componentType)', which does not exist.")
    }

    foreach ($band in $rule.bands)
    {
        if ($band.level -notin $complexityLevels) { $errors.Add("Complexity rule for '$($rule.componentType)' uses level '$($band.level)', which is not declared.") }
    }

    # A measured rule whose measure is not an attribute the component type declares will always
    # come back unrated, which looks identical to an extraction that did not reach far enough.
    if ($null -ne $rule.measure)
    {
        $type = $components.componentTypes | Where-Object { $_.id -eq $rule.componentType }
        if ($type -and ($type.PSObject.Properties.Name -contains 'attributes') -and $rule.measure -notin $type.attributes)
        {
            $warnings.Add("The complexity rule for '$($rule.componentType)' measures '$($rule.measure)', which is not in that type's declared attributes. Every component of that type will come back unrated.")
        }
    }
}

# ---------------------------------------------------------------- report --
# A section claiming to be generated with nothing behind it is the failure this contract exists
# to prevent: a readiness score produced from metadata reads exactly like a real one.
$report = Get-Contract -Name 'report-model'
$visualIds = $report.visuals | ForEach-Object { $_.id }

foreach ($section in $report.sections)
{
    if ($section.kind -notin @('generated', 'written', 'hybrid'))
    {
        $errors.Add("Report section '$($section.id)' has kind '$($section.kind)', which is not one of generated, written or hybrid.")
    }

    if ($section.kind -eq 'written' -and [string]::IsNullOrWhiteSpace($section.prompt))
    {
        $errors.Add("Report section '$($section.id)' is written by a consultant and gives them no prompt. An empty box with no question in it gets filled with whatever sounds right.")
    }

    if ($section.kind -eq 'hybrid' -and ([string]::IsNullOrWhiteSpace($section.generated) -or [string]::IsNullOrWhiteSpace($section.written)))
    {
        $errors.Add("Report section '$($section.id)' is hybrid and does not say which half is which.")
    }

    foreach ($visual in $section.visuals)
    {
        if ($visual -notin $visualIds) { $errors.Add("Report section '$($section.id)' shows visual '$visual', which is not declared.") }
    }
}

if ($report.benchmark.shipped)
{
    $errors.Add("A benchmark is marked as shipped in the box. An industry average is the most quotable figure in a report of this kind and the easiest to invent; it belongs per engagement, with a source and a date.")
}

# --------------------------------------------------------------------- report --
foreach ($warning in $warnings) { Write-Warning $warning }

if ($errors.Count -gt 0)
{
    Write-Host ''
    foreach ($problem in $errors) { Write-Host "  $problem" -ForegroundColor Red }
    Write-Host ''
    throw "$($errors.Count) contract problem(s). Nothing was generated."
}

Write-Host "Contracts consistent. $($componentIds.Count) component types, $($components.complexityRules.rules.Count) complexity rules, $($report.sections.Count) report sections, $($rules.rules.Count) rules ($($actionable.Count) actionable, all with acceptance criteria), $($modernisation.entries.Count) modernisation entries, $($stages.stages.Count) stages, $($sources.modes.Count) extraction modes, $($warnings.Count) warning(s)."
