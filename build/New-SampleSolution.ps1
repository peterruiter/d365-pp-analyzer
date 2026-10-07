<#
.SYNOPSIS
    Builds a synthetic exported solution with known findings in it.

.DESCRIPTION
    A solution file that looks like a real export and contains exactly the problems this
    product is supposed to find. It exists so the whole offline path can be run on a laptop
    with no client file, no environment, no credentials and nobody's permission.

    Every defect it plants is deliberate and listed below, so a run against this file has an
    expected answer rather than a plausible one. If a run produces fewer findings than this
    script planted, something is broken and you can see which thing.

    What it plants:

      1. A dialog                                     lifecycle.dialogPresent
      2. An activated classic background workflow     lifecycle.classicWorkflowInUse
      3. An activated real time workflow              lifecycle.classicWorkflowInUse
      4. A cloud flow of 12 actions, no failure path  quality.flowNoErrorHandling
      5. A cloud flow of 60 actions, nested 6 deep    quality.flowSize
      6. A hard coded external URL in a flow          alm.hardCodedEnvironmentValue
      7. An environment variable with a default       alm.environmentVariableWithDefault
      8. A plugin assembly outside sandbox isolation  quality.pluginNotSandboxed
      9. A form loading six script libraries          quality.formScriptOnLoadWeight
     10. A role with organisation level write         security.organisationLevelWrite
     11. A script doing only show and hide            architecture.proCodeWhereConfigWouldDo
     12. An API key in a web resource                 security.secretInDefinition
     13. Two publisher prefixes in one solution       alm.publisherPrefixSprawl
     14. A table with five logic mechanisms on it     architecture.logicSpread
     15. Custom columns on no form and no view        governance.orphanedColumn
     16. Components with no description               quality.missingDescription

.PARAMETER Path
    Where to write the zip. Defaults to samples/SampleSolution.zip.

.EXAMPLE
    ./build/New-SampleSolution.ps1

    Writes samples/SampleSolution.zip.

.EXAMPLE
    ./build/New-SampleSolution.ps1 -Path C:\temp\sample.zip
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Common.psm1') -Force

$root = Get-RepositoryRoot
if (-not $Path) { $Path = Join-Path $root 'samples/SampleSolution.zip' }

$staging = Join-Path ([System.IO.Path]::GetTempPath()) "ppa-sample-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $staging -Force
$null = New-Item -ItemType Directory -Path (Join-Path $staging 'Workflows') -Force
$null = New-Item -ItemType Directory -Path (Join-Path $staging 'WebResources') -Force

function Write-File
{
    param([string] $Relative, [string] $Content)

    $full = Join-Path $staging $Relative
    $directory = Split-Path -Parent $full
    if (-not (Test-Path $directory)) { $null = New-Item -ItemType Directory -Path $directory -Force }

    [System.IO.File]::WriteAllText($full, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

# ------------------------------------------------------------------ manifest --
Write-File 'solution.xml' @'
<?xml version="1.0" encoding="utf-8"?>
<ImportExportXml version="9.2.0.0" SolutionPackageVersion="9.2">
  <SolutionManifest>
    <UniqueName>SampleEstate</UniqueName>
    <LocalizedNames>
      <LocalizedName description="Sample Estate" languagecode="1033" />
    </LocalizedNames>
    <Version>1.0.0.0</Version>
    <Managed>0</Managed>
    <Publisher>
      <UniqueName>samplepublisher</UniqueName>
      <LocalizedNames>
        <LocalizedName description="Sample Publisher" languagecode="1033" />
      </LocalizedNames>
      <CustomizationPrefix>smp</CustomizationPrefix>
      <CustomizationOptionValuePrefix>10000</CustomizationOptionValuePrefix>
    </Publisher>
  </SolutionManifest>
</ImportExportXml>
'@

# ------------------------------------------------------------ cloud flow, small --
# Twelve actions, no failure path anywhere in the definition.
$smallActions = (1..12 | ForEach-Object { """Action_$_"": { ""type"": ""Compose"", ""inputs"": ""$_"" }" }) -join ",`n        "

Write-File 'Workflows/NotifyCustomer-11111111-1111-1111-1111-111111111111.json' @"
{
  "properties": {
    "connectionReferences": {
      "shared_commondataserviceforapps": {
        "connection": { "connectionReferenceLogicalName": "smp_dataverse" },
        "api": { "name": "shared_commondataserviceforapps" }
      }
    },
    "definition": {
      "triggers": {
        "When_a_row_is_added": { "type": "OpenApiConnection" }
      },
      "actions": {
        $smallActions,
        "Call_the_billing_system": {
          "type": "Http",
          "inputs": { "uri": "https://billing.acme-internal.example.com/api/v2/invoices" }
        }
      }
    }
  }
}
"@

# ------------------------------------------------------------- cloud flow, big --
# Sixty actions and six levels of nesting, which is the shape the size rule looks for and
# the shape a top level action count would report as having one action.
$inner = '"Leaf": { "type": "Compose", "inputs": "x" }'
for ($depth = 6; $depth -ge 1; $depth--)
{
    $siblings = (1..8 | ForEach-Object { """Step_${depth}_$_"": { ""type"": ""Compose"", ""inputs"": ""$_"" }" }) -join ",`n"
    $inner = @"
"Scope_$depth": {
  "type": "Scope",
  "actions": {
    $siblings,
    $inner
  }
}
"@
}

Write-File 'Workflows/ProcessOrder-22222222-2222-2222-2222-222222222222.json' @"
{
  "properties": {
    "definition": {
      "triggers": { "manual": { "type": "Request" } },
      "actions": {
        $inner
      }
    }
  }
}
"@

# ------------------------------------------------------- classic workflow XAML --
Write-File 'Workflows/AssignCase-33333333-3333-3333-3333-333333333333.xaml' @'
<Activity xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities">
  <Sequence>
    <If /><SetEntityProperty /><UpdateEntity /><AssignEntity /><SendEmail /><If /><CreateEntity />
  </Sequence>
</Activity>
'@

Write-File 'Workflows/ValidateOrder-44444444-4444-4444-4444-444444444444.xaml' @'
<Activity xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities">
  <Sequence><If /><SetEntityProperty /><UpdateEntity /></Sequence>
</Activity>
'@

# ---------------------------------------------------------------- web resources --
# A script doing nothing a business rule could not do.
Write-File 'WebResources/smp_simpleform.js' @'
function onLoad(executionContext) {
    var formContext = executionContext.getFormContext();
    var status = formContext.getAttribute("statuscode").getValue();
    formContext.getControl("smp_reason").setVisible(status === 2);
    formContext.getControl("smp_reason").setDisabled(status !== 2);
    formContext.getAttribute("smp_reason").setRequiredLevel(status === 2 ? "required" : "none");
}
'@

# A credential sitting in a file that is exported, versioned and readable by any maker.
Write-File 'WebResources/smp_integration.js' @'
var config = {
    endpoint: "https://partner.example.org/v1",
    api_key: "EXAMPLE_not_a_real_key_9f2b7c41e8a35d6094bf72e1c8a4",
    retry: 3
};
function send(payload) { return fetch(config.endpoint, { headers: { "x-api-key": config.api_key } }); }
'@

# Six libraries on one form, one of them large.
1..4 | ForEach-Object { Write-File "WebResources/smp_lib$_.js" ("// library $_`n" + ('x' * 2000)) }
Write-File 'WebResources/smp_framework.js' ("// framework`n" + ('y' * 180000))

# --------------------------------------------------------------- customizations --
$formLibraries = (1..4 | ForEach-Object { "<Library name=`"smp_lib$_.js`" libraryUniqueId=`"{0000000$_-0000-0000-0000-000000000000}`" />" }) -join "`n              "

# Fifteen custom columns, of which ten appear nowhere.
$columns = (1..15 | ForEach-Object {
    $prefix = if ($_ -le 12) { 'smp' } else { 'old' }   # two prefixes in one solution, deliberately
    $description = if ($_ -le 3) { "<Descriptions><Description description=`"A described column.`" languagecode=`"1033`" /></Descriptions>" } else { '' }
@"
          <attribute PhysicalName="${prefix}_field$_">
            <Type>nvarchar</Type>
            <Name>${prefix}_field$_</Name>
            <LogicalName>${prefix}_field$_</LogicalName>
            <RequiredLevel>none</RequiredLevel>
            <MaxLength>100</MaxLength>
            <IsCustomField>1</IsCustomField>
            <IsAuditEnabled>0</IsAuditEnabled>
            $description
          </attribute>
"@
}) -join "`n"

Write-File 'customizations.xml' @"
<?xml version="1.0" encoding="utf-8"?>
<ImportExportXml xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Entities>
    <Entity>
      <Name IsCustomEntity="1">smp_order</Name>
      <EntityInfo>
        <entity Name="smp_order">
          <attributes>
$columns
          </attributes>
        </entity>
      </EntityInfo>
      <FormXml>
        <forms type="main">
          <systemform>
            <formid>{aaaaaaaa-0000-0000-0000-000000000001}</formid>
            <type>2</type>
            <LocalizedNames><label description="Order" languagecode="1033" /></LocalizedNames>
            <form>
              <formLibraries>
              $formLibraries
              <Library name="smp_framework.js" libraryUniqueId="{00000009-0000-0000-0000-000000000000}" />
              <Library name="smp_simpleform.js" libraryUniqueId="{00000010-0000-0000-0000-000000000000}" />
              </formLibraries>
              <events><event name="onload" application="false" active="false" /></events>
              <tabs><tab name="general"><control id="smp_field1" /><control id="smp_field2" /></tab></tabs>
            </form>
          </systemform>
        </forms>
      </FormXml>
      <SavedQueries>
        <savedqueries>
          <savedquery>
            <savedqueryid>{bbbbbbbb-0000-0000-0000-000000000001}</savedqueryid>
            <isdefault>1</isdefault>
            <querytype>0</querytype>
            <LocalizedNames><LocalizedName description="Active Orders" languagecode="1033" /></LocalizedNames>
            <fetchxml>
              <fetch>
                <entity name="smp_order">
                  <attribute name="smp_field1" /><attribute name="smp_field2" />
                  <link-entity name="account" /><link-entity name="contact" />
                  <link-entity name="systemuser" /><link-entity name="smp_thing" />
                  <link-entity name="smp_other" /><link-entity name="smp_more" />
                </entity>
              </fetch>
            </fetchxml>
          </savedquery>
        </savedqueries>
      </SavedQueries>
    </Entity>
  </Entities>

  <Workflows>
    <Workflow WorkflowId="{33333333-3333-3333-3333-333333333333}" Name="Assign Case">
      <Category>0</Category><Mode>0</Mode><Scope>4</Scope><StateCode>1</StateCode>
      <PrimaryEntity>smp_order</PrimaryEntity>
      <TriggerOnCreate>1</TriggerOnCreate>
      <XamlFileName>/Workflows/AssignCase-33333333-3333-3333-3333-333333333333.xaml</XamlFileName>
    </Workflow>
    <Workflow WorkflowId="{44444444-4444-4444-4444-444444444444}" Name="Validate Order">
      <Category>0</Category><Mode>1</Mode><Scope>4</Scope><StateCode>1</StateCode>
      <PrimaryEntity>smp_order</PrimaryEntity>
      <XamlFileName>/Workflows/ValidateOrder-44444444-4444-4444-4444-444444444444.xaml</XamlFileName>
    </Workflow>
    <Workflow WorkflowId="{55555555-5555-5555-5555-555555555555}" Name="Old Order Wizard">
      <Category>1</Category><Mode>0</Mode><StateCode>0</StateCode>
      <PrimaryEntity>smp_order</PrimaryEntity>
    </Workflow>
    <Workflow WorkflowId="{66666666-6666-6666-6666-666666666666}" Name="Require Reason">
      <Category>2</Category><Mode>0</Mode><StateCode>1</StateCode>
      <PrimaryEntity>smp_order</PrimaryEntity>
    </Workflow>
    <Workflow WorkflowId="{11111111-1111-1111-1111-111111111111}" Name="Notify Customer">
      <Category>5</Category><Mode>0</Mode><StateCode>1</StateCode>
      <PrimaryEntity>smp_order</PrimaryEntity>
      <JsonFileName>/Workflows/NotifyCustomer-11111111-1111-1111-1111-111111111111.json</JsonFileName>
    </Workflow>
    <Workflow WorkflowId="{22222222-2222-2222-2222-222222222222}" Name="Process Order">
      <Category>5</Category><Mode>0</Mode><StateCode>1</StateCode>
      <JsonFileName>/Workflows/ProcessOrder-22222222-2222-2222-2222-222222222222.json</JsonFileName>
    </Workflow>
  </Workflows>

  <WebResources>
    <WebResource><WebResourceId>{cccccccc-0000-0000-0000-000000000001}</WebResourceId><Name>smp_simpleform.js</Name><WebResourceType>3</WebResourceType><FileName>/WebResources/smp_simpleform.js</FileName></WebResource>
    <WebResource><WebResourceId>{cccccccc-0000-0000-0000-000000000002}</WebResourceId><Name>smp_integration.js</Name><WebResourceType>3</WebResourceType><FileName>/WebResources/smp_integration.js</FileName></WebResource>
    <WebResource><WebResourceId>{cccccccc-0000-0000-0000-000000000003}</WebResourceId><Name>smp_framework.js</Name><WebResourceType>3</WebResourceType><FileName>/WebResources/smp_framework.js</FileName></WebResource>
    <WebResource><WebResourceId>{cccccccc-0000-0000-0000-000000000004}</WebResourceId><Name>smp_lib1.js</Name><WebResourceType>3</WebResourceType><FileName>/WebResources/smp_lib1.js</FileName></WebResource>
    <WebResource><WebResourceId>{cccccccc-0000-0000-0000-000000000005}</WebResourceId><Name>smp_lib2.js</Name><WebResourceType>3</WebResourceType><FileName>/WebResources/smp_lib2.js</FileName></WebResource>
    <WebResource><WebResourceId>{cccccccc-0000-0000-0000-000000000006}</WebResourceId><Name>smp_lib3.js</Name><WebResourceType>3</WebResourceType><FileName>/WebResources/smp_lib3.js</FileName></WebResource>
    <WebResource><WebResourceId>{cccccccc-0000-0000-0000-000000000007}</WebResourceId><Name>smp_lib4.js</Name><WebResourceType>3</WebResourceType><FileName>/WebResources/smp_lib4.js</FileName></WebResource>
  </WebResources>

  <connectionreferences>
    <connectionreference connectionreferencelogicalname="smp_dataverse">
      <connectionreferencedisplayname>Dataverse</connectionreferencedisplayname>
      <connectorid>/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps</connectorid>
    </connectionreference>
  </connectionreferences>

  <environmentvariabledefinitions>
    <environmentvariabledefinition schemaname="smp_BillingEndpoint">
      <displayname>Billing endpoint</displayname>
      <type>100000000</type>
      <defaultvalue>https://billing-dev.acme-internal.example.com</defaultvalue>
    </environmentvariabledefinition>
  </environmentvariabledefinitions>

  <Roles>
    <Role roleid="{dddddddd-0000-0000-0000-000000000001}" name="Order Manager">
      <RolePrivileges>
        <RolePrivilege name="prvWriteAccount" level="Global" />
        <RolePrivilege name="prvDeleteContact" level="Global" />
        <RolePrivilege name="prvReadsmp_order" level="Basic" />
      </RolePrivileges>
    </Role>
  </Roles>

  <SolutionPluginAssemblies>
    <PluginAssembly PluginAssemblyId="{eeeeeeee-0000-0000-0000-000000000001}" FullName="Sample.Plugins">
      <Name>Sample.Plugins</Name>
      <IsolationMode>1</IsolationMode>
      <SourceType>1</SourceType>
      <Version>1.0.0.0</Version>
      <PluginTypes><PluginType><Name>Sample.Plugins.OnOrderCreate</Name></PluginType></PluginTypes>
    </PluginAssembly>
  </SolutionPluginAssemblies>

  <optionsets>
    <optionset Name="smp_orderstatus" OptionSetId="{ffffffff-0000-0000-0000-000000000001}">
      <options><option value="1" /><option value="2" /><option value="3" /></options>
    </optionset>
  </optionsets>
</ImportExportXml>
"@

# ------------------------------------------------------------------------ zip --
if ($PSCmdlet.ShouldProcess($Path, 'Write sample solution'))
{
    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path $directory)) { $null = New-Item -ItemType Directory -Path $directory -Force }
    if (Test-Path $Path) { Remove-Item $Path -Force }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $Path)
}

Remove-Item $staging -Recurse -Force

Write-Host "Wrote $Path"
Write-Host ''
Write-Host 'Sixteen findings are planted in it. A run producing fewer means something is broken,'
Write-Host 'and the list at the top of this script says which one to look at.'
