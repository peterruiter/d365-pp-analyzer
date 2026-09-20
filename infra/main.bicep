// ===========================================================================
// The Contact Center Analyzer environment.
//
// Deployed per engagement or per client, which is why every name is derived
// from one prefix: two engagements in the same subscription must not collide.
//
// The database, the vault, monitoring and the containers. The API and the worker
// run the same image: one thing to build, one thing to tag, one thing to delete.
// ===========================================================================

targetScope = 'resourceGroup'

@description('Prefix for every resource name. Lowercase letters and numbers only.')
@minLength(3)
@maxLength(20)
param namePrefix string

@description('Azure region.')
param location string = resourceGroup().location

@description('Object id of the Entra principal that administers the database.')
param administratorObjectId string

@description('Display name of that principal.')
param administratorName string

@description('Whether the administrator principal is a user, a group or an application.')
@allowed(['User', 'Group', 'Application'])
param administratorType string = 'User'

@description('Client IP allowed through the SQL firewall, so a developer machine can apply migrations.')
param clientIpAddress string = ''

@description('Whether the vault refuses to be purged before its retention expires. Off for development, on for a vault holding credentials belonging to a client.')
param vaultPurgeProtection bool = false

@description('Image the API and the worker run. Empty on a first deployment, before anything has been built.')
param containerImage string = ''

@description('Who may sign in. "organizations" is any Entra tenant, which is what a consultancy needs. A tenant id restricts it to that one tenant. Empty leaves authentication off, which is only ever right on a developer machine.')
param azureAdTenantId string = ''

@description('Entra application id for sign in.')
param azureAdClientId string = ''

@description('The directory the app registration lives in, for the credentials this product issues as itself.')
param azureAdHomeTenantId string = ''

@description('Entra client secret for the sign in flow.')
@secure()
param azureAdClientSecret string = ''

@description('When the Entra client secret expires, as a date. Not a secret; it is the thing the health page counts down to.')
param azureAdClientSecretExpiresUtc string = ''

@description('Syncfusion licence key for the PDF report. Empty leaves the PDF off; the workbook and the runbook are unaffected.')
@secure()
param syncfusionLicenseKey string = ''

@description('Who a reader with no access is told to ask.')
param adminContactEmail string = ''

@description('Sign-in name of the first global administrator. Without one, nobody can be admitted.')
param initialGlobalAdminUpn string = ''

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    namePrefix: namePrefix
    administratorObjectId: administratorObjectId
    administratorName: administratorName
    administratorType: administratorType
    clientIpAddress: clientIpAddress
  }
}

// One user assigned identity for the API, the worker and the database.
//
// Declared here rather than inside a module because two modules need it: the vault grants it
// a role and the containers run as it. Creating it in either one made them depend on each
// other, which Bicep rejects and which would have been wrong anyway.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-identity'
  location: location
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    namePrefix: namePrefix
    // The administrator creates connections from their own machine, so they write
    // secrets as well as read them. The API does the same when a consultant saves a
    // connection in the web application.
    secretsOfficers: [
      { objectId: administratorObjectId, principalType: administratorType }
      { objectId: identity.properties.principalId, principalType: 'ServicePrincipal' }
    ]
    // The containers and nobody else. A person has no reason to wrap anything with this
    // key: it protects the product's own sign-in state, not anything a consultant reads.
    cryptoKeyUsers: [
      { objectId: identity.properties.principalId, principalType: 'ServicePrincipal' }
    ]
    purgeProtection: vaultPurgeProtection
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    namePrefix: namePrefix
    identityPrincipalId: identity.properties.principalId
  }
}

module openAi 'modules/openai.bicep' = {
  name: 'openai'
  params: {
    location: location
    namePrefix: namePrefix
    identityPrincipalId: identity.properties.principalId
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module containers 'modules/containerapps.bicep' = {
  name: 'containerapps'
  params: {
    namePrefix: namePrefix
    location: location
    identityId: identity.id
    identityClientId: identity.properties.clientId
    identityPrincipalId: identity.properties.principalId
    logAnalyticsCustomerId: monitoring.outputs.logAnalyticsCustomerId
    logAnalyticsKey: monitoring.outputs.logAnalyticsKey
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    image: containerImage
    // Active Directory Default, so the container authenticates as its managed identity and
    // there is no password anywhere. Connect Timeout is generous because a paused serverless
    // database takes about a minute to wake and the credential chain is not instant either.
    sqlConnectionString: '${sql.outputs.connectionString}Authentication=Active Directory Default;Connect Timeout=90;'
    keyVaultUri: keyVault.outputs.vaultUri
    uploadContainerUri: storage.outputs.uploadContainerUri
    openAiEndpoint: openAi.outputs.endpoint
    openAiDeployment: openAi.outputs.deploymentName
    dataProtectionBlobUri: storage.outputs.dataProtectionBlobUri
    dataProtectionKeyUri: keyVault.outputs.dataProtectionKeyUri
    azureAdTenantId: azureAdTenantId
    azureAdClientId: azureAdClientId
    azureAdHomeTenantId: azureAdHomeTenantId
    azureAdClientSecret: azureAdClientSecret
    azureAdClientSecretExpiresUtc: azureAdClientSecretExpiresUtc
    syncfusionLicenseKey: syncfusionLicenseKey
    adminContactEmail: adminContactEmail
    initialGlobalAdminUpn: initialGlobalAdminUpn
  }
}

@description('Fully qualified SQL server name.')
output sqlServer string = sql.outputs.serverFullyQualifiedDomainName

@description('Database name.')
output sqlDatabase string = sql.outputs.databaseName

@description('Connection string. No secret in it; authentication is Entra.')
// Carries the authentication mode, because the server is Entra only and a string without it
// fails the login with 18456, which reads like a missing permission rather than a missing
// clause. This output is what Deploy-Infrastructure.ps1 prints for a person to paste into
// Initialize-Database.ps1, so it has to be a string that actually connects.
output sqlConnectionString string = '${sql.outputs.connectionString}Authentication=Active Directory Default;'

@description('Key Vault name.')
output keyVaultName string = keyVault.outputs.vaultName

@description('What goes in KeyVault:Uri. Without it the product refuses to store a credential rather than putting one in the database.')
output keyVaultUri string = keyVault.outputs.vaultUri

@description('What goes in OpenAi:Endpoint and OpenAi:Deployment. Without them every estimate is a band default.')
output openAiEndpoint string = openAi.outputs.endpoint
output openAiDeployment string = openAi.outputs.deploymentName

@description('Where the API keeps the keys that sign a sign-in, and the vault key they are wrapped with.')
output dataProtectionBlobUri string = storage.outputs.dataProtectionBlobUri
output dataProtectionKeyUri string = keyVault.outputs.dataProtectionKeyUri

@description('Where an uploaded solution file goes.')
output uploadContainerUri string = storage.outputs.uploadContainerUri

@description('Where the product is.')
output apiUrl string = containers.outputs.apiUrl

@description('Container registry, for Publish-Container.ps1.')
output containerRegistry string = containers.outputs.registryName

@description('Container app running the API.')
output apiAppName string = containers.outputs.apiAppName

@description('Container app running the worker.')
output workerAppName string = containers.outputs.workerAppName

@description('Name of the identity both containers run as. SQL needs it to create a user.')
output identityName string = identity.name
