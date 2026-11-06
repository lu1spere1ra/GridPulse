// GridPulse on Azure: Container Apps + PostgreSQL Flexible Server + Storage (ADR-0005).
//
// Two-stage deployment (see docs/deploy-azure.md):
//   1. deployApps=false  -> identity, registry, storage, database, monitoring
//   2. images are pushed to the registry
//   3. deployApps=true   -> Container Apps environment, apps, migrator job
targetScope = 'resourceGroup'

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('Short environment name used in resource names, e.g. "demo".')
@maxLength(8)
param environmentName string = 'demo'

@description('Deploy the container apps (stage 3). Images must already be in the registry.')
param deployApps bool = false

@description('Image tag to deploy, usually the commit SHA.')
param imageTag string = 'latest'

param postgresAdminLogin string = 'gridpulse'

@secure()
@description('PostgreSQL administrator password. Passed by the workflow from a GitHub secret; never stored in the repo.')
param postgresAdminPassword string

@description('Simulated meters in Azure (kept small to keep the demo cheap).')
@minValue(1)
@maxValue(5000)
param meterCount int = 200

var prefix = 'gridpulse-${environmentName}'
var unique = uniqueString(resourceGroup().id)

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-id'
  location: location
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { location: location, prefix: prefix }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    location: location
    name: 'gridpulse${unique}'
    pullPrincipalId: identity.properties.principalId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    name: 'gridpulse${take(unique, 13)}'
    principalId: identity.properties.principalId
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    location: location
    name: '${prefix}-pg-${take(unique, 6)}'
    administratorLogin: postgresAdminLogin
    administratorPassword: postgresAdminPassword
  }
}

module apps 'modules/apps.bicep' = if (deployApps) {
  name: 'apps'
  params: {
    location: location
    prefix: prefix
    logAnalyticsWorkspaceName: monitoring.outputs.workspaceName
    identityId: identity.id
    identityClientId: identity.properties.clientId
    registryLoginServer: registry.outputs.loginServer
    imageTag: imageTag
    databaseConnectionString: 'Host=${postgres.outputs.fqdn};Database=${postgres.outputs.databaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require'
    storageAccountName: storage.outputs.name
    storageBlobEndpoint: storage.outputs.blobEndpoint
    meterCount: meterCount
  }
}

output registryName string = registry.outputs.name
output registryLoginServer string = registry.outputs.loginServer
output postgresServerName string = postgres.outputs.name
output apiUrl string = deployApps ? apps!.outputs.apiUrl : ''
output webUrl string = deployApps ? apps!.outputs.webUrl : ''
output reportsUrl string = deployApps ? apps!.outputs.reportsUrl : ''
output migratorJobName string = deployApps ? apps!.outputs.migratorJobName : ''
