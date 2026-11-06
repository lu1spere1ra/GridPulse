using 'main.bicep'

param environmentName = 'demo'
param meterCount = 200
// postgresAdminPassword, imageTag and deployApps are supplied by the deploy workflow.
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD', '')
