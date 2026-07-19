targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string


param shopdeliveryApiExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@description('OpenID Connect authority used to validate customer access tokens')
param authenticationAuthority string

@description('Audience expected in customer access tokens')
param authenticationAudience string

@secure()
@description('SQL Server administrator password — stored in azd env, never in source control')
param sqlAdminPassword string

// Tags that should be applied to all resources.
// 
// Note that 'azd-service-name' tags should be applied separately to service host resources.
// Example usage:
//   tags: union(tags, { 'azd-service-name': <service name in azure.yaml> })
var tags = {
  'azd-env-name': environmentName
}

// Organize resources in a resource group
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  scope: rg
  name: 'resources'
  params: {
    location: location
    tags: tags
    principalId: principalId
    principalType: principalType
    authenticationAuthority: authenticationAuthority
    authenticationAudience: authenticationAudience
    shopdeliveryApiExists: shopdeliveryApiExists
    sqlAdminPassword: sqlAdminPassword
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output AZURE_RESOURCE_SHOPDELIVERY_API_ID string = resources.outputs.AZURE_RESOURCE_SHOPDELIVERY_API_ID
output AZURE_SQL_SERVER_FQDN string = resources.outputs.AZURE_SQL_SERVER_FQDN
output AZURE_SQL_DATABASE_NAME string = resources.outputs.AZURE_SQL_DATABASE_NAME
