// ===========================================================================
// Azure SQL: one logical server and one serverless database.
//
// Serverless rather than provisioned, and that is the whole cost story. A
// migration tool is used in bursts: a discovery runs for an hour and then the
// database sits idle for a fortnight until the next engagement. Serverless
// pauses itself after an hour of inactivity and bills storage only, which for
// this workload is the difference between a few pounds a month and a few tens.
//
// The trade is a cold start of roughly a minute when a paused database is first
// touched. The worker retries, and a consultant opening the application after a
// quiet fortnight waits once. That is the right way round.
// ===========================================================================

@description('Azure region. Keep client configuration in the region the client expects it to be in.')
param location string = resourceGroup().location

@description('Prefix for resource names. Lowercase letters and numbers only.')
@minLength(3)
@maxLength(20)
param namePrefix string

@description('Object id of the Entra principal that administers the server. A user or a group.')
param administratorObjectId string

@description('Display name of that principal, which is what shows in the portal.')
param administratorName string

@description('Whether the administrator principal is a user, a group or an application.')
@allowed(['User', 'Group', 'Application'])
param administratorType string = 'User'

@description('Largest the database may grow. 32 GB is far more than configuration ever needs; this is a guard rail, not a target.')
param maxSizeBytes int = 34359738368

@description('Minutes of inactivity before the database pauses itself. 60 is the minimum Azure allows.')
param autoPauseDelayMinutes int = 60

@description('Smallest number of vCores the database scales down to while in use.')
param minCapacity string = '0.5'

@description('Largest number of vCores the database scales up to.')
param maxCapacity int = 1

@description('Client IP allowed through the firewall, so a developer machine can apply migrations. Empty adds no rule.')
param clientIpAddress string = ''

var serverName = '${namePrefix}-sql'
var databaseName = '${namePrefix}-db'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // No SQL administrator login and no password, anywhere. Entra only.
    //
    // A password would have to be generated, stored, rotated and kept out of
    // logs, and every one of those is a chance to get it wrong. Removing the
    // option removes the whole class of mistake, and it means a leaked
    // connection string is not on its own enough to read a client's data.
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: administratorName
      sid: administratorObjectId
      principalType: administratorType
      tenantId: subscription().tenantId
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    // GP_S is General Purpose Serverless. The _1 is the vCore ceiling.
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: maxCapacity
  }
  properties: {
    autoPauseDelay: autoPauseDelayMinutes
    minCapacity: json(minCapacity)
    maxSizeBytes: maxSizeBytes
    // Local redundancy, deliberately. This database holds a client's contact
    // centre configuration, which is reproducible by rerunning a discovery. Paying
    // for geo redundant backups of derived data is paying twice for the same thing.
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

// Lets the Container App reach the database without a virtual network. The 0.0.0.0
// rule is Azure's convention for "Azure services", not for the public internet.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource allowClient 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!empty(clientIpAddress)) {
  parent: sqlServer
  name: 'AllowDeployingMachine'
  properties: {
    startIpAddress: clientIpAddress
    endIpAddress: clientIpAddress
  }
}

@description('Fully qualified server name.')
output serverFullyQualifiedDomainName string = sqlServer.properties.fullyQualifiedDomainName

@description('Database name.')
output databaseName string = database.name

@description('Connection string with no secret in it. Authentication is Entra, so there is nothing here to leak.')
output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${database.name};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
