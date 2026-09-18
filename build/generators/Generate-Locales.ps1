<#
.SYNOPSIS
    Generates the locale catalogue from the locales contract.

.DESCRIPTION
    Reads build/contracts/locales.json and writes the list of languages the product is
    offered in, plus the rule for resolving one a reader asked for.

    Generated because three things need the same list and must not disagree: the API that
    serves language bundles, the web application's language picker, and the documentation
    build that produces a folder per language.

    Called by build/Invoke-CodeGen.ps1.

.PARAMETER OutputPath
    Where the generated file goes. Defaults to the Domain project's Generated folder.

.EXAMPLE
    ./build/generators/Generate-Locales.ps1

    Regenerates after adding a language.
#>
[CmdletBinding(SupportsShouldProcess)]
param([string] $OutputPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..' 'Common.psm1') -Force

$root = Get-RepositoryRoot
if (-not $OutputPath) { $OutputPath = Join-Path $root 'src/PowerPete.Analyzer.Domain/Generated' }

$contract = Get-Contract -Name 'locales'
$namespace = 'PowerPete.Analyzer.Domain'

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("namespace $namespace;")
$lines.Add('')
$lines.Add('/// <summary>One language the product is offered in.</summary>')
$lines.Add('/// <param name="Code">Short code, for example <c>nl</c>.</param>')
$lines.Add('/// <param name="EnglishName">Name in English, for a list an English speaker reads.</param>')
$lines.Add('/// <param name="NativeName">Name in its own language, which is what the picker shows.</param>')
$lines.Add('/// <param name="Culture">Culture for formatting numbers and dates.</param>')
$lines.Add('/// <param name="DocumentationFolder">Where this language''s documentation lives.</param>')
$lines.Add('public sealed record Locale(string Code, string EnglishName, string NativeName, string Culture, string DocumentationFolder);')
$lines.Add('')
$lines.Add('/// <summary>')
$lines.Add('/// Every language, and the rule for resolving one somebody asked for.')
$lines.Add('/// </summary>')
$lines.Add('/// <remarks>')
$lines.Add('/// English is the fallback and the only language guaranteed complete. A half translated')
$lines.Add('/// screen is worth shipping; one showing a resource key to a client is not.')
$lines.Add('/// </remarks>')
$lines.Add('public static class LocaleCatalogue')
$lines.Add('{')
$lines.Add('    /// <summary>Every language the product is offered in.</summary>')
$lines.Add('    public static IReadOnlyList<Locale> All { get; } =')
$lines.Add('    [')
foreach ($locale in $contract.locales)
{
    $lines.Add("        new(`"$($locale.code)`", `"$($locale.englishName)`", `"$($locale.nativeName)`", `"$($locale.culture)`", `"$($locale.documentationFolder)`"),")
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    /// <summary>The language everything falls back to.</summary>')
$lines.Add("    public const string DefaultCode = `"$($contract.default)`";")
$lines.Add('')
$lines.Add('    /// <summary>The resource namespaces, one file per namespace per language.</summary>')
$lines.Add('    public static IReadOnlyList<string> Namespaces { get; } =')
$lines.Add('    [')
foreach ($ns in $contract.namespaces) { $lines.Add("        `"$($ns.id)`",") }
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    /// <summary>The default language.</summary>')
$lines.Add('    public static Locale Default { get; } = All.First(locale => locale.Code == DefaultCode);')
$lines.Add('')
$lines.Add('    /// <summary>')
$lines.Add('    /// Resolves whatever a reader asked for, falling back rather than failing.')
$lines.Add('    /// </summary>')
$lines.Add('    /// <remarks>')
$lines.Add('    /// Takes the language part of a culture, so a browser asking for nl-BE gets Dutch')
$lines.Add('    /// rather than English. Nobody asking for Belgian Dutch wants to be shown English.')
$lines.Add('    /// </remarks>')
$lines.Add('    /// <param name="code">What was asked for, which may be null or nonsense.</param>')
$lines.Add('    public static Locale Resolve(string? code)')
$lines.Add('    {')
$lines.Add('        if (string.IsNullOrWhiteSpace(code)) return Default;')
$lines.Add('')
$lines.Add('        var exact = All.FirstOrDefault(locale => string.Equals(locale.Code, code, StringComparison.OrdinalIgnoreCase));')
$lines.Add('        if (exact is not null) return exact;')
$lines.Add('')
$lines.Add('        var language = code.Split(''-'')[0];')
$lines.Add('        return All.FirstOrDefault(locale => string.Equals(locale.Code, language, StringComparison.OrdinalIgnoreCase)) ?? Default;')
$lines.Add('    }')
$lines.Add('}')

Write-GeneratedFile -Path (Join-Path $OutputPath 'LocaleCatalogue.g.cs') `
    -Content (($lines -join "`n") + "`n") -Contract 'locales.json'

Write-Host "Locales generated: $(@($contract.locales).Count) languages, $(@($contract.namespaces).Count) namespaces."
