@description('Azure region.')
param location string

@description('Globally unique server name.')
param name string

param administratorLogin string

@secure()
param administratorPassword string

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    // 16 rather than 17: TimescaleDB availability on Flexible Server lags behind new major versions.
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    storage: { storageSizeGB: 32, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

// TimescaleDB must be allow-listed and preloaded. Azure ships the Apache-2 edition, so the migrator
// creates plain views instead of continuous aggregates (ADR-0001). Changing shared_preload_libraries
// needs a server restart, which the deploy workflow performs when required.
resource allowExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'azure.extensions'
  properties: { value: 'TIMESCALEDB', source: 'user-override' }
}

resource preload 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'shared_preload_libraries'
  properties: { value: 'timescaledb', source: 'user-override' }
  dependsOn: [allowExtensions]
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: 'gridpulse'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// Public endpoint restricted to Azure services, TLS enforced by the server. A VNet with private
// access is the production upgrade (ADR-0005); it is left out to keep the demo deployment small.
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: server
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

output fqdn string = server.properties.fullyQualifiedDomainName
output name string = server.name
output databaseName string = database.name
