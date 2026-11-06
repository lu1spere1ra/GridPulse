@description('Azure region.')
param location string

@description('Resource name prefix.')
param prefix string

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

output workspaceName string = logs.name
output customerId string = logs.properties.customerId
