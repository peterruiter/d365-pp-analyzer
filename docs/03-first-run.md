# 3. First run

What to do the first time you open this in Visual Studio. In order, because each step
depends on the one before it.

Nothing in this repository has ever been executed. Expect the first two steps to fail.
That is what they are for.

## What you need

| Thing | Version | Where |
|---|---|---|
| .NET SDK | 9.0 | https://dotnet.microsoft.com/download |
| PowerShell | 7.4 or later | `winget install Microsoft.PowerShell` |
| Azure CLI | Any current | Only for deploying, not for building |

No Azure subscription, no Power Platform environment and no credentials are needed for
anything on this page.

## 1. Check the contracts

```
./build/Test-Contracts.ps1
```

Reads all eight contracts and cross references them. It writes nothing and connects to
nothing. It should report the counts and no errors.

If it fails, the message names the file and the line to fix. Nothing further will work until
this passes, which is deliberate: a broken reference does not fail a build, it fails at run
time as a rule that silently matches nothing.

## 2. Generate and build

```
./build/Invoke-CodeGen.ps1
```

Runs the contract check again, then three generators, then `dotnet build`.

**This has never run.** The likely failures, in the order you will hit them:

- String escaping in the generated C# literals. The generators quote contract text into C#
  strings, and the contract text contains apostrophes and quotation marks.
- Nullable annotations. Warnings are errors in this repository.
- Enum member names. `ConvertTo-PascalCase` upper cases the first letter only, so a contract
  id of `lowCode` becomes `LowCode` and one of `notMigratable` becomes `NotMigratable`. Check
  the generated enums match what the C# expects.

To iterate on a generator without building each time:

```
./build/Invoke-CodeGen.ps1 -SkipBuild
```

To see what it would write without writing anything:

```
./build/Invoke-CodeGen.ps1 -WhatIf
```

## 3. Run the tests

```
dotnet test
```

The contract tests read the JSON from disk rather than the generated code, so they catch a
contract that was edited without regenerating.

`The_generated_catalogue_and_the_contract_agree` is the one that fails when you forget step 2.

## 4. Build the sample solution

```
./build/New-SampleSolution.ps1
```

Writes `samples/SampleSolution.zip`: a synthetic export containing sixteen deliberate
findings, listed at the top of the script. No client file, no environment, nobody's
permission.

This is what you run the extractor against first. A run that finds fewer than sixteen tells
you which reader is broken, because the list says what should have been found.

## 5. Run the whole offline path at it

```
dotnet run --project src/PowerPete.Analyzer.Jobs -- analyse samples/SampleSolution.zip
```

Reads, resolves, applies every rule the file can answer, scores, and prints what it could not
assess. No database, no environment, no credentials, no configuration.

Band estimates only. There is no model call in this command, deliberately: it has to work
before anything else is set up.

Two other commands need nothing either:

```
dotnet run --project src/PowerPete.Analyzer.Jobs -- rules
dotnet run --project src/PowerPete.Analyzer.Jobs -- components
```

Check the workflow classification in the output before anything else. The sample plants one
background workflow, one real time workflow, one dialog, one business rule and two cloud
flows. If the reader does not produce exactly that, the category mapping is wrong, and a wrong
category mapping does not throw: it classifies a business rule as a classic workflow and the
report then tells a client to migrate forty things that are not there.

Then read the *not assessed* list. It should be long, and every line should say the same thing:
this rule needs metadata, runtime evidence or the checker, and a file carries none of them.
That list being short would mean the reach check is broken.

## 6. Then a real export

Export one unmanaged solution from an environment you own and run the reader at it. This is
the step that answers, in an afternoon and with no security review:

- the workflow category numbers
- the plugin isolation and source type codes
- the web resource type codes
- the connection reference element names
- whether the form XML carries script libraries where this reader looks for them

Every one of those is currently taken from documentation rather than from a file, and every
one of them fails silently when it is wrong.

## What does not exist yet

`analyzer work` prints a message and exits. The pipeline, the stages, the stores and the
readers all exist; what is missing is the composition root that reads configuration, resolves
a connection string and a Key Vault and hands the services to the runner.

No API, no web application, no microsite and no exports. `STATE.md` has the order they come in
and what each one is waiting on.
