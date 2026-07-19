@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Tags that will be applied to all resources')
param tags object = {}

param shopdeliveryApiExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@description('OpenID Connect authority used to validate customer access tokens')
param authenticationAuthority string

@description('Audience expected in customer access tokens')
param authenticationAudience string

@description('SQL Server administrator login name')
param sqlAdminLogin string = 'shopadmin'

@secure()
@description('SQL Server administrator password')
param sqlAdminPassword string

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)

// Monitor application with Azure Monitor
module monitoring 'br/public:avm/ptn/azd/monitoring:0.1.0' = {
  name: 'monitoring'
  params: {
    logAnalyticsName: '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    applicationInsightsName: '${abbrs.insightsComponents}${resourceToken}'
    applicationInsightsDashboardName: '${abbrs.portalDashboards}${resourceToken}'
    location: location
    tags: tags
  }
}
// Container registry
module containerRegistry 'br/public:avm/res/container-registry/registry:0.1.1' = {
  name: 'registry'
  params: {
    name: '${abbrs.containerRegistryRegistries}${resourceToken}'
    location: location
    tags: tags
    publicNetworkAccess: 'Enabled'
    roleAssignments:[
      {
        principalId: shopdeliveryApiIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
      }
    ]
  }
}

// Container apps environment
module containerAppsEnvironment 'br/public:avm/res/app/managed-environment:0.4.5' = {
  name: 'container-apps-environment'
  params: {
    logAnalyticsWorkspaceResourceId: monitoring.outputs.logAnalyticsWorkspaceResourceId
    name: '${abbrs.appManagedEnvironments}${resourceToken}'
    location: location
    zoneRedundant: false
  }
}

module shopdeliveryApiIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: 'shopdeliveryApiidentity'
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}shopdeliveryApi-${resourceToken}'
    location: location
  }
}
// ── Azure SQL Server + Database ──────────────────────────────────────────────

module sqlServer 'br/public:avm/res/sql/server:0.4.0' = {
  name: 'sql-server'
  params: {
    name: '${abbrs.sqlServers}${resourceToken}'
    location: location
    tags: tags
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    // Grant the API managed identity as an Entra admin so it can connect passwordless.
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: false
      login: 'shopdeliveryApi-mi'
      principalType: 'Application'
      sid: shopdeliveryApiIdentity.outputs.clientId
      tenantId: subscription().tenantId
    }
    databases: [
      {
        name: 'shopdelivery'
        skuName: 'Basic'
        skuTier: 'Basic'
        maxSizeBytes: 2147483648  // 2 GB
      }
    ]
    firewallRules: [
      {
        // Allow other Azure services (Container App) to reach the server.
        name: 'AllowAllWindowsAzureIps'
        startIpAddress: '0.0.0.0'
        endIpAddress: '0.0.0.0'
      }
    ]
  }
}

// ─────────────────────────────────────────────────────────────────────────────

module shopdeliveryApiFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'shopdeliveryApi-fetch-image'
  params: {
    exists: shopdeliveryApiExists
    name: 'shopdelivery-api'
  }
}

module shopdeliveryApi 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'shopdeliveryApi'
  params: {
    name: 'shopdelivery-api'
    ingressTargetPort: 8080
    scaleMinReplicas: 1
    scaleMaxReplicas: 10
    secrets: {
      secureList:  [
      ]
    }
    containers: [
      {
        image: shopdeliveryApiFetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
        name: 'main'
        resources: {
          cpu: json('0.5')
          memory: '1.0Gi'
        }
        env: [
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: monitoring.outputs.applicationInsightsConnectionString
          }
          {
            name: 'AZURE_CLIENT_ID'
            value: shopdeliveryApiIdentity.outputs.clientId
          }
          {
            name: 'PORT'
            value: '8080'
          }
          {
            name: 'Authentication__Authority'
            value: authenticationAuthority
          }
          {
            name: 'Authentication__Audience'
            value: authenticationAudience
          }
          {
            // Passwordless: the container uses its managed identity (AZURE_CLIENT_ID) to authenticate.
            name: 'ConnectionStrings__Sql'
            value: 'Server=tcp:${sqlServer.outputs.name}.database.windows.net,1433;Initial Catalog=shopdelivery;Authentication=Active Directory Managed Identity;User Id=${shopdeliveryApiIdentity.outputs.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
          }
        ]
      }
    ]
    managedIdentities:{
      systemAssigned: false
      userAssignedResourceIds: [shopdeliveryApiIdentity.outputs.resourceId]
    }
    registries:[
      {
        server: containerRegistry.outputs.loginServer
        identity: shopdeliveryApiIdentity.outputs.resourceId
      }
    ]
    environmentResourceId: containerAppsEnvironment.outputs.resourceId
    location: location
    tags: union(tags, { 'azd-service-name': 'shopdelivery-api' })
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AZURE_RESOURCE_SHOPDELIVERY_API_ID string = shopdeliveryApi.outputs.resourceId
output AZURE_SQL_SERVER_FQDN string = '${sqlServer.outputs.name}.database.windows.net'
output AZURE_SQL_DATABASE_NAME string = 'shopdelivery'
