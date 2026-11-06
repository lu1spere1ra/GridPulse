@description('Azure region.')
param location string

@description('Globally unique registry name (alphanumeric).')
param name string

@description('Principal that pulls images (the apps\' user-assigned identity).')
param pullPrincipalId string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false // apps pull with their managed identity, never with admin credentials
  }
}

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullPrincipalId, acrPullRoleId)
  scope: registry
  properties: {
    principalId: pullPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

output loginServer string = registry.properties.loginServer
output name string = registry.name
