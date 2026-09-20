// ===========================================================================
// Key Vault: where a client's source system credentials live.
//
// Not the database. A connection row is read by every screen, written into
// every export and printed in every diagnostic, and a client's service principal client
// secret has no business being in any of them. The row carries a reference and
// the value lives here.
//
// RBAC rather than access policies. Access policies are per-vault lists that
// nobody reviews and that no subscription-wide report can see; role assignments
// show up everywhere Azure reports on access, which is what makes an over-
// privileged principal findable rather than merely recorded.
// ===========================================================================

@description('Azure region. Keep client credentials in the region the client expects them to be in.')
param location string = resourceGroup().location

@description('Prefix for resource names. Lowercase letters and numbers only.')
@minLength(3)
@maxLength(20)
param namePrefix string

@description('''
Who may read, write and delete secrets: the people and services that create connections.

Each entry is { objectId, principalType }, and principalType is not optional. A role
assignment for a managed identity created moments earlier fails with PrincipalNotFound
because the directory has not replicated yet, and naming the type is what tells Azure to
stop looking it up and take your word for it. Found exactly that way, on the first
deployment that created the identity and the vault together.
''')
param secretsOfficers array = []

@description('Who may only read secrets. Same shape. What a reader needs and nothing more.')
param secretsUsers array = []

@description('''
Who may wrap and unwrap with the data protection key. Same shape as the two above.

In practice this is the containers' managed identity and nobody else. A person never needs
it: the key protects the product's own sign-in state, not anything a consultant reads.
''')
param cryptoKeyUsers array = []

@description('''
Whether the vault refuses to be purged before its retention expires.

Off for a development environment, because it cannot be undone and it makes a resource
group impossible to clean up for ninety days. On for anything holding a real client's
credentials, where somebody deleting the vault should not also be able to erase the
evidence.
''')
param purgeProtection bool = false

@description('Days a deleted secret can still be recovered.')
@minValue(7)
@maxValue(90)
param retentionDays int = 90

// Vault names are globally unique and capped at 24 characters, which is shorter than
// most prefixes plus a suffix. The hash keeps it unique without making it unreadable.
var vaultName = take('${namePrefix}-kv-${uniqueString(resourceGroup().id)}', 24)

// Built-in role definition ids. Constants published by Azure, not names, because a
// display name is localised and a built-in role can be renamed.
var secretsOfficer = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
var secretsUser = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

// Crypto User can wrap and unwrap with a key and cannot read it, export it or delete it.
// That is exactly what encrypting the data protection keys needs, and deliberately less
// than Crypto Officer: the identity should never be able to remove the key that everything
// it has written depends on.
var cryptoUser = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    // Not optional in Azure any more, and it is what makes a connection deleted by
    // mistake recoverable rather than gone.
    enableSoftDelete: true
    softDeleteRetentionInDays: retentionDays
    enablePurgeProtection: purgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource officers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principal in secretsOfficers: {
    scope: vault
    // Deterministic, so redeploying updates the assignment rather than failing on a
    // name that already exists.
    name: guid(vault.id, principal.objectId, secretsOfficer)
    properties: {
      roleDefinitionId: secretsOfficer
      principalId: principal.objectId
      principalType: principal.principalType
    }
  }
]

resource users 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principal in secretsUsers: {
    scope: vault
    name: guid(vault.id, principal.objectId, secretsUser)
    properties: {
      roleDefinitionId: secretsUser
      principalId: principal.objectId
      principalType: principal.principalType
    }
  }
]

// The key the API's data protection keys are encrypted with.
//
// A key rather than a secret, because the API never sees it: it sends the data protection
// key material to the vault to be wrapped and gets ciphertext back. The material never
// leaves Azure's HSM boundary and nothing in the product can print it.
//
// This is what makes it safe to write those keys to blob storage at all. The blob role is
// scoped to the whole storage account, so anybody granted access to the uploads container
// can also read the container these live in; encrypted, that gets them nothing.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048

    // Wrap and unwrap only. The key exists to protect other keys and has no business
    // signing or encrypting anything else.
    keyOps: ['wrapKey', 'unwrapKey']
    attributes: {
      enabled: true
    }
  }
}

resource cryptoUsers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principal in cryptoKeyUsers: {
    // Scoped to the key rather than to the vault. The identity can wrap with this one key
    // and has no relationship with any other key anybody puts here later.
    scope: dataProtectionKey
    name: guid(dataProtectionKey.id, principal.objectId, cryptoUser)
    properties: {
      roleDefinitionId: cryptoUser
      principalId: principal.objectId
      principalType: principal.principalType
    }
  }
]

@description('Vault name.')
output vaultName string = vault.name

@description('The key the data protection keys are wrapped with, for DataProtection:KeyUri.')
output dataProtectionKeyUri string = dataProtectionKey.properties.keyUriWithVersion

@description('What goes in KeyVault:Uri. There is no secret in this; reaching it still needs a role.')
output vaultUri string = vault.properties.vaultUri
