// ===========================================================================
// The registry, the Container Apps environment, the API and the worker.
//
// The API container also serves the React workspace from wwwroot. One image,
// one thing to deploy, one thing to delete, which matters for a tool deployed
// per engagement.
//
// The worker is a separate container app rather than a Container Apps Job. A
// job is started on demand and this worker polls: it has to be there when a
// consultant presses the button, not started by the thing that presses it. It
// costs one always-on replica of the smallest size, and it means a run does not
// wait for a cold start before it begins reading a client's estate.
// ===========================================================================

@description('Prefix for resource names.')
param namePrefix string

@description('Azure region.')
param location string = resourceGroup().location

@description('Resource id of the shared identity the containers run as.')
param identityId string

@description('Client id of that identity, which is what tells DefaultAzureCredential which one to use.')
param identityClientId string

@description('Object id of that identity, for the registry role assignment.')
param identityPrincipalId string

@description('Workspace id for the environment logs.')
param logAnalyticsCustomerId string

@description('Shared key for that workspace.')
@secure()
param logAnalyticsKey string

@description('Application Insights connection string.')
param appInsightsConnectionString string = ''

@description('Image both containers run. Empty on a first deployment, before anything has been built.')
param image string = ''

@description('Database connection string. No secret in it; authentication is Entra.')
param sqlConnectionString string

@description('Where credentials live. Without it the product refuses to store one rather than putting it in the database.')
param keyVaultUri string

@description('The blob the API keeps its data protection keys in.')
param dataProtectionBlobUri string = ''

@description('The vault key those keys are wrapped with before they are written.')
param dataProtectionKeyUri string = ''

@description('Where an uploaded solution file goes.')
param uploadContainerUri string = ''

@description('The Azure OpenAI resource that estimates a finding. Empty means every estimate is the band default, which the report says out loud.')
param openAiEndpoint string = ''

@description('Which deployment on it, recorded against every estimate the model produced.')
param openAiDeployment string = ''

@description('Entra tenant for sign in. Empty leaves authentication off, which is only ever right on a developer machine.')
param azureAdTenantId string = ''

@description('Entra application id for sign in.')
param azureAdClientId string = ''

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

@description('''
Sign-in name of the first global administrator.

Seeded on every access check, not once at startup, and it only ever grants. A container that
started before this was set would otherwise stay locked out until somebody restarted it, and
the person locked out is by definition the one who cannot.
''')
param initialGlobalAdminUpn string = ''

// Placeholder for the first deployment, when the registry is empty because nothing has been
// built yet. Deploying an app with no image fails; deploying this one starts and serves a
// holding page until Publish-Container.ps1 replaces it.
var startingImage = empty(image) ? 'mcr.microsoft.com/k8se/quickstart:latest' : image

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: replace('${namePrefix}acr${take(uniqueString(resourceGroup().id, namePrefix), 6)}', '-', '')
  location: location
  sku: { name: 'Basic' }
  properties: {
    // No admin user. The containers pull as a managed identity, so there is no registry
    // password to store, rotate or leak.
    adminUserEnabled: false
  }
}

// The identity is created in main.bicep rather than here, and passed in. It has to exist
// before the vault, because the vault grants it a role, and the containers need the vault's
// URI. Creating it here made those two modules depend on each other.
var acrPull = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource pull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identityId, acrPull)
  properties: {
    roleDefinitionId: acrPull
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsKey
      }
    }
  }
}

// AZURE_CLIENT_ID is what tells DefaultAzureCredential which of the assigned identities to
// use. Without it a container with a user assigned identity authenticates as nothing in
// particular and the failure reads as a permissions problem.
var commonEnv = [
  { name: 'AZURE_CLIENT_ID', value: identityClientId }
  { name: 'ConnectionStrings__Analyzer', value: sqlConnectionString }
  { name: 'KeyVault__Uri', value: keyVaultUri }
  // The API writes an uploaded solution here and the worker reads it back, so both need it.
  { name: 'ANALYZER_UPLOAD_CONTAINER', value: uploadContainerUri }
  // Where the API keeps the keys that sign an interactive sign-in's state, and the vault
  // key they are wrapped with. Without both, ASP.NET writes them inside the container:
  // they die with the replica, they are not shared between the three the API scales to,
  // and a sign-in returning to a different replica than it started on cannot be read.
  { name: 'DataProtection__BlobUri', value: dataProtectionBlobUri }
  { name: 'DataProtection__KeyUri', value: dataProtectionKeyUri }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
  { name: 'AzureAd__TenantId', value: azureAdTenantId }
  { name: 'AzureAd__ClientId', value: azureAdClientId }
  { name: 'AzureAd__Instance', value: 'https://login.microsoftonline.com/' }
  { name: 'AzureAd__CallbackPath', value: '/signin-oidc' }
  { name: 'AzureAd__ClientSecretExpiresUtc', value: azureAdClientSecretExpiresUtc }
  { name: 'Support__AdminContact', value: adminContactEmail }
  { name: 'Access__InitialGlobalAdminUpn', value: initialGlobalAdminUpn }
  { name: 'Access__GlobalAdminContactEmail', value: adminContactEmail }

  // The estimator's model. Read by the worker, which is the only thing that estimates;
  // the API is given them so the health page can say whether estimates are model or band.
  //
  // These were set by hand for an afternoon and were not in this file, which meant the
  // next infrastructure deployment would have quietly turned the model off again and
  // every estimate would have gone back to being a band default. The report would still
  // have said so, in a caveat, at the bottom, which is how nobody would have noticed.
  { name: 'OpenAi__Endpoint', value: openAiEndpoint }
  { name: 'OpenAi__Deployment', value: openAiDeployment }
]

// Held by the platform rather than written into the container definition. An absent secret
// leaves the variable out entirely rather than setting it empty, so authentication is either
// configured or plainly off.
var secretValues = concat(
  empty(azureAdClientSecret) ? [] : [
    { name: 'azuread-client-secret', value: azureAdClientSecret }
  ],
  // The same treatment for the same reason. A key written into the container definition is a
  // key in every deployment history and every az output, and this one is licensed rather than
  // ours to leak.
  empty(syncfusionLicenseKey) ? [] : [
    { name: 'syncfusion-license-key', value: syncfusionLicenseKey }
  ])

var secretEnv = concat(
  empty(azureAdClientSecret) ? [] : [
    { name: 'AzureAd__ClientSecret', secretRef: 'azuread-client-secret' }
  ],
  empty(syncfusionLicenseKey) ? [] : [
    { name: 'Syncfusion__LicenseKey', secretRef: 'syncfusion-license-key' }
  ])

var containerEnv = concat(commonEnv, secretEnv)

var registries = [
  {
    server: registry.properties.loginServer
    identity: identityId
  }
]

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: secretValues
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      // Configured whatever image is running. Making this conditional on a real image
      // meant the first deployment left it off, and the first "az containerapp update"
      // to a real tag failed with UNAUTHORIZED because nothing told the app how to pull.
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'api'
          image: startingImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: containerEnv
          probes: [
            {
              type: 'Liveness'
              // Liveness never touches the database. One that did would restart a perfectly
              // good container every time the serverless database pauses, which it does once
              // an hour by design.
              httpGet: { path: '/health/live', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 15
              // Generous, because a paused serverless database takes about a minute to wake
              // and the first request after a quiet fortnight is the one that wakes it.
              failureThreshold: 10
            }
          ]
        }
      ]
      scale: {
        // One, so the app never cold starts into a failed activation in front of somebody.
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-worker'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: secretValues
      // No ingress. Nothing should be able to reach the worker over HTTP: it takes its
      // instructions from the database and from nowhere else.
      // Configured whatever image is running. Making this conditional on a real image
      // meant the first deployment left it off, and the first "az containerapp update"
      // to a real tag failed with UNAUTHORIZED because nothing told the app how to pull.
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: startingImage
          // "work" is the poll loop. Without it the dispatcher prints its help and exits
          // zero, which the platform treats as a container that finished and restarts, over
          // and over, with a successful exit code and a help screen in the log.
          command: empty(image) ? null : ['/app/jobs-entrypoint.sh']
          args: empty(image) ? null : ['work']
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: containerEnv
        }
      ]
      scale: {
        // Exactly one. Two workers would halve the wall clock of a discovery and double the
        // load on a client's platform, and the platform is the thing that cannot be scaled.
        // The claim is safe against two, but that is a safety net rather than a plan.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

@description('Where the product is.')
output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'

@description('Registry name, for Publish-Container.ps1.')
output registryName string = registry.name

@description('Registry login server.')
output registryLoginServer string = registry.properties.loginServer

@description('Container app name for the API.')
output apiAppName string = api.name

@description('Container app name for the worker.')
output workerAppName string = worker.name
