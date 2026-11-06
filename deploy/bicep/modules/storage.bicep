@description('Azure region.')
param location string

@description('Globally unique storage account name (3-24 lowercase alphanumerics).')
param name string

@description('Principal of the reports Function (blob read/write, Functions host locks).')
param principalId string

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // Entra ID (managed identity) only: no account keys in configuration
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
}

resource reports 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'reports'
}

// Owner (not just Contributor): the Functions host also writes its timer-trigger locks to blobs.
var blobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'

resource blobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, principalId, blobDataOwnerRoleId)
  scope: account
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataOwnerRoleId)
  }
}

output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
