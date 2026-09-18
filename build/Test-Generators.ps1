<#
.SYNOPSIS
    Runs every generator against a throwaway folder and checks the C# it produced.

.DESCRIPTION
    A generator emits text. Nothing checks that text is valid C# until the compiler sees it,
    and by then the error points at a generated file nobody wrote and which is regenerated
    before anybody can read it.

    This runs each generator into a temporary folder and checks the obvious ways emitted C#
    goes wrong: unbalanced braces, an odd number of quotes on a line, an unterminated verbatim
    string, and a literal that still contains a raw newline.

    It is not a compiler. It catches the class of bug that costs an afternoon, which is a
    contract string containing a character the quoting did not handle.

.EXAMPLE
    ./build/Test-Generators.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Common.psm1') -Force

$temporary = Join-Path ([System.IO.Path]::GetTempPath()) "ppa-gen-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $temporary -Force

$problems = [System.Collections.Generic.List[string]]::new()

try
{
    foreach ($generator in Get-ChildItem (Join-Path $PSScriptRoot 'generators') -Filter 'Generate-*.ps1')
    {
        & $generator.FullName -OutputPath $temporary | Out-Null
    }

    foreach ($file in Get-ChildItem $temporary -Filter '*.g.cs')
    {
        $content = Get-Content $file.FullName -Raw
        $lines = $content -split "`n"

        $open = ([regex]::Matches($content, '\{')).Count
        $close = ([regex]::Matches($content, '\}')).Count
        if ($open -ne $close) { $problems.Add("$($file.Name): $open opening braces and $close closing ones.") }

        for ($index = 0; $index -lt $lines.Count; $index++)
        {
            $line = $lines[$index]

            # Escaped quotes removed first, so only the ones that delimit a literal are counted.
            $stripped = $line -replace '\\.', ''
            $quotes = ([regex]::Matches($stripped, '"')).Count

            if ($quotes % 2 -ne 0)
            {
                $problems.Add("$($file.Name):$($index + 1): a string literal is not closed on its own line. $($line.Trim())")
            }
        }

        if ($content -match '@"[^"]*$') { $problems.Add("$($file.Name): an unterminated verbatim string.") }
    }

    $count = (Get-ChildItem $temporary -Filter '*.g.cs').Count

    if ($problems.Count -gt 0)
    {
        foreach ($problem in $problems) { Write-Host "  $problem" -ForegroundColor Red }
        throw "$($problems.Count) problem(s) in the generated C#. Nothing was written to the repository."
    }

    Write-Host "$count generated file(s) look structurally sound. This is not a compiler; run dotnet build next."
}
finally
{
    Remove-Item $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
