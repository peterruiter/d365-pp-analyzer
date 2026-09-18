// ===========================================================================
// Log Analytics and Application Insights. Container Apps needs both.
//
// Thirty days of retention, because a migration tool is used in bursts and the
// question asked after an engagement is "what did the apply write", which the
// ledger answers, not "what did the container log in March".
// ===========================================================================

@description('Prefix for resource names.')
param namePrefix string

@description('Azure region.')
param location string = resourceGroup().location

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

@description('Workspace id the Container Apps environment sends logs to.')
output logAnalyticsCustomerId string = logAnalytics.properties.customerId

#disable-next-line outputs-should-not-contain-secrets
@description('Shared key for that workspace. Container Apps takes it as a secure parameter.')
output logAnalyticsKey string = logAnalytics.listKeys().primarySharedKey

@description('Application Insights connection string.')
output appInsightsConnectionString string = appInsights.properties.ConnectionString
