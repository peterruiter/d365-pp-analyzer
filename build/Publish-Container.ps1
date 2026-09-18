<#
.SYNOPSIS
    Builds the container image in Azure Container Registry and points the deployment at it.

.DESCRIPTION
    Runs the code generator, builds the Dockerfile remotely with "az acr build", then updates
    the API and the worker container apps to the new tag.

    The build happens in the registry rather than on your machine, so you do not need Docker
    installed and a slow connection uploads source rather than layers.

    Only the image changes. Deploy-Infrastructure.ps1 is what reconciles the environment with
    the Bicep template, and using this script for that would need the full parameter set.

.PARAMETER ResourceGroup
    The resource group the deployment is in. Defaults to ANALYZER_RESOURCE_GROUP.

.PARAMETER Registry
    Container registry name. Defaults to ANALYZER_REGISTRY, then to the only registry in the
    resource group.

.PARAMETER ImageTag
    Tag for the image. Defaults to the VERSION file plus a UTC timestamp, so two builds of
    the same version can be told apart.

.PARAMETER SkipBuild
    Deploy a tag that is already in the registry, to promote an earlier build or to finish a
    deployment after the build succeeded and the client fell over.

.PARAMETER SkipDeploy
    Build and push without updating the container apps.

.EXAMPLE
    ./build/Publish-Container.ps1 -ResourceGroup rg-ccanalyzer-dev

    Builds and deploys.

.EXAMPLE
    ./build/Publish-Container.ps1 -ResourceGroup rg-ccanalyzer-dev -SkipDeploy

    Builds the image and leaves the deployment alone.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ResourceGroup = $env:ANALYZER_RESOURCE_GROUP,
    [string] $Registry = $env:ANALYZER_REGISTRY,
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$')][string] $ImageTag,
    [switch] $SkipBuild,
    [switch] $SkipDeploy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Common.psm1') -Force

Assert-Command -Name az -InstallHint 'Install from https://aka.ms/installazurecli' | Out-Null

if (-not $ResourceGroup)
{
    throw 'No resource group. Pass -ResourceGroup, or set ANALYZER_RESOURCE_GROUP.'
}

# The build log the registry streams back contains a tick character. On a console that is
# not UTF-8, which is most Windows consoles, the Azure CLI crashes trying to print it and
# reports a build failure for an image it has already built and pushed.
$env:PYTHONIOENCODING = 'utf-8'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Get-RepositoryRoot

if (-not $Registry)
{
    $Registry = az acr list --resource-group $ResourceGroup --query '[0].name' --output tsv
    if (-not $Registry) { throw "No container registry in '$ResourceGroup'. Run Deploy-Infrastructure.ps1 first." }
}

if (-not $ImageTag)
{
    $ImageTag = "$(Get-RepositoryVersion)-$(Get-Date -Format 'yyyyMMddHHmmss')"
}

$repository = 'powerplatform-analyzer'

Write-Host "Resource group : $ResourceGroup"
Write-Host "Registry       : $Registry"
Write-Host "Tag            : $ImageTag"
Write-Host ''

if (-not $SkipBuild)
{
    # The generated code is gitignored, so it reaches the image only because the build
    # context is packed from this working tree. Running the generator first is what stops an
    # image being built from a stale contract. The Dockerfile checks the files arrived.
    Write-Host 'Generating code from the contracts.'
    & (Join-Path $PSScriptRoot 'Invoke-CodeGen.ps1')
    Write-Host ''

    if ($PSCmdlet.ShouldProcess("$Registry/${repository}:$ImageTag", 'Build the image'))
    {
        # Noted before the build starts, so the fallback below cannot mistake the previous
        # run's failure for this one's. That happened: a build was abandoned on the strength
        # of a status belonging to the attempt before it.
        $startedAt = (Get-Date).ToUniversalTime()

        Push-Location $root
        try
        {
            az acr build --registry $Registry --image "${repository}:$ImageTag" --file Dockerfile .

            # A non-zero exit code from az acr build does not mean the build failed.
            #
            # The registry does the work and streams its log back, and the client can die
            # part way through printing it while the build carries on and pushes the image.
            # Believing the exit code means throwing away a good image and building it again.
            # So ask the registry instead: the tag is either there or it is not.
            if ($LASTEXITCODE -ne 0)
            {
                Write-Host ''
                Write-Host 'The build client exited with an error. The registry may still be building, so waiting.'

                $deadline = (Get-Date).AddMinutes(30)
                $pushed = $null

                while (-not $pushed -and (Get-Date) -lt $deadline)
                {
                    $pushed = az acr repository show-tags --name $Registry --repository $repository `
                        --query "[?@=='$ImageTag'] | [0]" --output tsv

                    if ($pushed) { break }

                    # A run that has genuinely failed should not cost another half hour of
                    # waiting. Only a run that started after this build began counts: the
                    # newest run overall may be the previous attempt, still sitting there
                    # marked Failed, and believing it throws away a build that is going fine.
                    $recent = az acr task list-runs --registry $Registry --top 5 `
                        --query "[?startTime >= '$($startedAt.ToString('o'))'] | [0].status" --output tsv

                    if ($recent -and @('Failed', 'Canceled', 'Error', 'Timeout') -contains $recent)
                    {
                        throw "The build failed in the registry with status '$recent'. See: az acr task logs --registry $Registry"
                    }

                    Start-Sleep -Seconds 15
                }

                if (-not $pushed) { throw 'The image did not reach the registry within thirty minutes.' }
                Write-Host 'The image reached the registry. Continuing.'
            }
        }
        finally
        {
            Pop-Location
        }
    }
}
else
{
    $exists = az acr repository show-tags --name $Registry --repository $repository `
        --query "[?@=='$ImageTag'] | [0]" --output tsv

    if (-not $exists) { throw "There is no image tagged '$ImageTag' in $Registry. Run without -SkipBuild." }
    Write-Host "Using the existing tag '$ImageTag'."
}

if ($SkipDeploy)
{
    Write-Host ''
    Write-Host 'Deployment skipped.'
    Write-Host "    Next: rerun without -SkipDeploy to point the container apps at $ImageTag."
    return
}

$loginServer = az acr show --name $Registry --resource-group $ResourceGroup --query loginServer --output tsv
if (-not $loginServer) { throw "Could not read the login server for '$Registry'." }

$image = "$loginServer/${repository}:$ImageTag"

# Both apps, because they run the same image. Updating one and not the other is how an API
# ends up offering a screen the worker's pipeline does not implement, with both version
# numbers agreeing.
foreach ($app in @(az containerapp list --resource-group $ResourceGroup --query '[].name' --output tsv))
{
    if (-not $PSCmdlet.ShouldProcess($app, "Update to $ImageTag")) { continue }

    Write-Host ''
    Write-Host "Updating $app"
    az containerapp update --name $app --resource-group $ResourceGroup --image $image --output none

    if ($LASTEXITCODE -ne 0) { throw "Could not update '$app'." }
}

$url = az containerapp list --resource-group $ResourceGroup `
    --query "[?configuration.ingress != null] | [0].properties.configuration.ingress.fqdn" --output tsv

Write-Host ''
Write-Host "Deployed $ImageTag."

if ($url)
{
    Write-Host ''
    Write-Host "    The product is at https://$url"
}

Write-Host ''
Write-Host '    Next: ./build/Initialize-Database.ps1 if the schema has changed.'
