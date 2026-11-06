@description('Azure region.')
param location string

param prefix string

@description('Log Analytics workspace for container logs.')
param logAnalyticsWorkspaceName string

@description('User-assigned identity used by every app (image pulls, blob access).')
param identityId string
param identityClientId string

param registryLoginServer string
param imageTag string

@secure()
@description('Npgsql connection string for the GridPulse database.')
param databaseConnectionString string

param storageAccountName string
param storageBlobEndpoint string

@minValue(1)
@maxValue(5000)
param meterCount int

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
  }
}

var identity = { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
var registries = [{ server: registryLoginServer, identity: identityId }]
var dbSecret = [{ name: 'db-connection', value: databaseConnectionString }]
var dbEnv = [{ name: 'ConnectionStrings__GridPulse', secretRef: 'db-connection' }]
var mqttEnv = [
  { name: 'Mqtt__Host', value: 'mosquitto' }
  { name: 'Mqtt__Port', value: '1883' }
]
var probes = [
  { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 10 }
  { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 10 }
]

func image(server string, name string, tag string) string => '${server}/gridpulse/${name}:${tag}'

// MQTT broker, reachable only inside the environment (internal TCP ingress). Anonymous access is
// acceptable only because nothing outside the environment can connect; device authentication
// (client certificates or Event Grid's MQTT broker) is the production path.
resource mosquitto 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'mosquitto'
  location: location
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      ingress: { external: false, transport: 'tcp', targetPort: 1883, exposedPort: 1883 }
      // Not secret: Container Apps mounts files only from secrets or Azure Files, and a secret volume
      // is the simplest way to ship the same mosquitto.conf that docker compose uses.
      #disable-next-line use-secure-value-for-secure-inputs
      secrets: [{ name: 'mosquitto-conf', value: loadTextContent('../../mosquitto/mosquitto.conf') }]
    }
    template: {
      containers: [
        {
          name: 'mosquitto'
          image: 'docker.io/library/eclipse-mosquitto:2.0.22'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          volumeMounts: [{ volumeName: 'config', mountPath: '/mosquitto/config' }]
        }
      ]
      volumes: [{ name: 'config', storageType: 'Secret', secrets: [{ secretRef: 'mosquitto-conf', path: 'mosquitto.conf' }] }]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// Exactly one replica: window state is per meter and in memory (ADR-0002).
resource ingestion 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ingestion'
  location: location
  identity: identity
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: { registries: registries, secrets: dbSecret }
    template: {
      containers: [
        {
          name: 'ingestion'
          image: image(registryLoginServer, 'ingestion', imageTag)
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: concat(dbEnv, mqttEnv)
          probes: probes
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
  dependsOn: [mosquitto]
}

// Sticky sessions: SignalR's negotiate and connect requests must reach the same replica. No
// backplane is needed because every replica receives every live message (ADR-0006).
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'api'
  location: location
  identity: identity
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      registries: registries
      secrets: dbSecret
      ingress: { external: true, targetPort: 8080, transport: 'auto', stickySessions: { affinity: 'sticky' } }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image(registryLoginServer, 'api', imageTag)
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(dbEnv, mqttEnv)
          probes: probes
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '100' } } }]
      }
    }
  }
  dependsOn: [mosquitto]
}

// Blazor Server keeps a circuit per tab, so it needs sticky sessions too.
resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'web'
  location: location
  identity: identity
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      registries: registries
      ingress: { external: true, targetPort: 8080, transport: 'auto', stickySessions: { affinity: 'sticky' } }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: image(registryLoginServer, 'web', imageTag)
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [{ name: 'Api__BaseUrl', value: 'https://${api.properties.configuration.ingress.fqdn}/' }]
          probes: probes
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
}

resource simulator 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'simulator'
  location: location
  identity: identity
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: { registries: registries }
    template: {
      containers: [
        {
          name: 'simulator'
          image: image(registryLoginServer, 'simulator', imageTag)
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(mqttEnv, [
            { name: 'Simulator__MeterCount', value: string(meterCount) }
            { name: 'Simulator__ApiBaseUrl', value: 'https://${api.properties.configuration.ingress.fqdn}/' }
          ])
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// Azure Functions hosted in the same Container Apps environment. Storage is reached with the
// managed identity: no account keys anywhere (the storage account has shared keys disabled).
resource reports 'Microsoft.App/containerApps@2025-07-01' = {
  name: 'reports'
  location: location
  kind: 'functionapp'
  identity: identity
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      registries: registries
      secrets: dbSecret
      ingress: { external: true, targetPort: 80, transport: 'auto' }
    }
    template: {
      containers: [
        {
          name: 'reports'
          image: image(registryLoginServer, 'reports', imageTag)
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(dbEnv, [
            { name: 'AzureWebJobsStorage__accountName', value: storageAccountName }
            { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
            { name: 'AzureWebJobsStorage__clientId', value: identityClientId }
            { name: 'ReportStorage__ServiceUri', value: storageBlobEndpoint }
            { name: 'AZURE_CLIENT_ID', value: identityClientId }
            { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
          ])
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}

// One-shot migrations, started by the deploy workflow after every deployment.
resource migrator 'Microsoft.App/jobs@2024-03-01' = {
  name: 'migrator'
  location: location
  identity: identity
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 1
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: registries
      secrets: dbSecret
    }
    template: {
      containers: [
        {
          name: 'migrator'
          image: image(registryLoginServer, 'migrator', imageTag)
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: dbEnv
        }
      ]
    }
  }
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
output reportsUrl string = 'https://${reports.properties.configuration.ingress.fqdn}'
output migratorJobName string = migrator.name
