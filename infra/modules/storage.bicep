// ============================================================== uploads ==
// Where an exported solution file goes.
//
// The offline mode is the one that gets past a security review in week one, and it needs
// somewhere to put a zip. This is that somewhere and nothing else: no static site, no
// tables, no queues. A container with one job is a container whose access policy is
// obvious.
//
// Nothing here is public. Blob public access is off at the account, the container is
// private, and the only principal that can read or write is the product's own managed
// identity, through Entra rather than through a key. Shared key access is disabled
// outright, so there is no connection string to leak and no key to rotate.

@description('Where to create it.')
param location string

@description('Prefix every resource in this deployment shares.')
param namePrefix string

@description('The managed identity both containers run as.')
param identityPrincipalId string

// Storage account names are three to twenty four characters, lower case letters and digits
// only, and globally unique. The prefix is stripped of anything else and a hash of the
// resource group keeps it from colliding with somebody else's.
var accountName = take('${toLower(replace(namePrefix, '-', ''))}up${uniqueString(resourceGroup().id)}', 24)

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: accountName
  location: location
  sku: {
    // Locally redundant. A solution export is evidence that can be re-exported from the
    // client's environment in a minute, so paying for geo-redundancy would be paying to
    // protect a copy of something somebody else already has.
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false

    // No account keys. The identity authenticates through Entra, so there is no shared
    // secret in configuration, nothing to rotate and nothing that keeps working after
    // somebody leaves.
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      // A week. Long enough that deleting the wrong connection is recoverable, short
      // enough that a client's solution file is not sitting here a year later.
      enabled: true
      days: 7
    }
  }
}

resource uploads 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'uploads'
  properties: {
    publicAccess: 'None'
  }
}

// Storage Blob Data Contributor, scoped to this account rather than the resource group.
// The API writes an uploaded file and the worker reads it back; neither needs to manage
// the account itself, which is why this is the data plane role and not Contributor.
var blobDataContributor = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource grant 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, identityPrincipalId, blobDataContributor)
  properties: {
    roleDefinitionId: blobDataContributor
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

@description('The uploads container, as the product addresses it.')
output uploadContainerUri string = '${account.properties.primaryEndpoints.blob}${uploads.name}'

@description('The account, for anybody looking for the files.')
output accountName string = account.name
