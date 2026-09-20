// ============================================================ the model ==
// Where an estimate with a rationale comes from.
//
// This product uses a language model for exactly one thing: putting hours on a finding the
// rules have already found. Whether something is a problem is decided by the rule
// catalogue, by Microsoft's checker and by arithmetic, none of which involve a model. What
// a model is good at is the part a consultant would otherwise do from experience — "this
// particular plug-in, on this table, with these steps, is about a day" — and what it is bad
// at is being sure, which is why the estimator rejects a range it cannot plan with and
// flags one it does not believe.
//
// It was not deployed for the first weeks of this product's life, and nothing said so on
// screen. Every estimate in every report was the band default for the rule, the score
// carried a caveat counting them, and the picker offered "Model estimates" with nothing
// behind it. A capability declared and not implemented is this product's most frequent
// defect; this is the infrastructure half of fixing that one.

@description('Where to create it.')
param location string

@description('Prefix every resource in this deployment shares.')
param namePrefix string

@description('The managed identity the worker runs as.')
param identityPrincipalId string

@description('Which model. Small on purpose: this is constrained JSON extraction against a rendered prompt, not open conversation.')
param modelName string = 'gpt-4.1-mini'

@description('Which version of it. Pinned, because two runs of the same estate must not produce different numbers.')
param modelVersion string = '2025-04-14'

@description('Thousands of tokens a minute. One finding is about 1.5k, so this is roughly thirty findings a minute.')
param capacity int = 50

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: '${namePrefix}-openai'
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    // Required for Entra authentication: without a custom subdomain the resource only
    // answers on the regional endpoint, which takes a key and nothing else.
    customSubDomainName: '${namePrefix}-openai'

    // No keys. The same argument as the storage account: a key in a container's
    // environment is a key in every diagnostic dump and every process listing on the host,
    // and there is a managed identity three lines below that needs no rotating.
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: modelName
  sku: {
    // The EU data zone, not global. An estimate prompt carries a client's component names,
    // its evidence and the rule that fired: it is a description of somebody's estate, and
    // where that gets processed is a question a client is entitled to ask and this product
    // already answers for the checker.
    name: 'DataZoneStandard'
    capacity: capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }

    // Never on its own.
    //
    // The service default is OnceNewDefaultVersionAvailable, which moves a deployment to a
    // newer model the moment Microsoft makes one the default. That would make the pinned
    // version above a lie and, worse, it would change what this product estimates without
    // anybody deciding to: a client who kept the first report and asks why the second says
    // something different deserves a better answer than "the model moved".
    //
    // Upgrading is then a change somebody makes on purpose, in this file, with the prompt
    // version beside it in the provenance record of every estimate.
    versionUpgradeOption: 'NoAutoUpgrade'

    // Named rather than left to the service, so a policy changing underneath is visible
    // here as a difference rather than as answers that quietly start being refused.
    raiPolicyName: 'Microsoft.DefaultV2'
  }
}

// Cognitive Services OpenAI User: call the model, read nothing about the resource itself.
// Not Contributor, which would let the worker create deployments.
var openAiUser = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')

resource grant 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, identityPrincipalId, openAiUser)
  properties: {
    roleDefinitionId: openAiUser
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

@description('What goes in OpenAi:Endpoint. Without it every estimate is the band default and the report says so.')
output endpoint string = account.properties.endpoint

@description('What goes in OpenAi:Deployment, and what gets recorded against every estimate the model produced.')
output deploymentName string = deployment.name
