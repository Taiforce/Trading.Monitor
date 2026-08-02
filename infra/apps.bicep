targetScope = 'resourceGroup'

param location string = resourceGroup().location
param containerEnvironmentName string
param managedIdentityResourceId string
param registryLoginServer string
param keyVaultUri string
param webImage string
param workerImage string
param openAiEnabled bool = false
param alphaVantageEnabled bool = false
param cryptoPanicEnabled bool = false
param customDomainName string = 'TradingMonitor.taiforce.com'

var secretIdentity = managedIdentityResourceId
var commonSecrets = [
  {
    name: 'database-connection-string'
    keyVaultUrl: '${keyVaultUri}secrets/database-connection-string'
    identity: secretIdentity
  }
  {
    name: 'admin-username'
    keyVaultUrl: '${keyVaultUri}secrets/admin-username'
    identity: secretIdentity
  }
  {
    name: 'admin-password'
    keyVaultUrl: '${keyVaultUri}secrets/admin-password'
    identity: secretIdentity
  }
  {
    name: 'openai-api-key'
    keyVaultUrl: '${keyVaultUri}secrets/openai-api-key'
    identity: secretIdentity
  }
  {
    name: 'binance-api-key'
    keyVaultUrl: '${keyVaultUri}secrets/binance-api-key'
    identity: secretIdentity
  }
  {
    name: 'binance-api-secret'
    keyVaultUrl: '${keyVaultUri}secrets/binance-api-secret'
    identity: secretIdentity
  }
  {
    name: 'alpha-vantage-api-key'
    keyVaultUrl: '${keyVaultUri}secrets/alpha-vantage-api-key'
    identity: secretIdentity
  }
  {
    name: 'cryptopanic-auth-token'
    keyVaultUrl: '${keyVaultUri}secrets/cryptopanic-auth-token'
    identity: secretIdentity
  }
  {
    name: 'telegram-bot-token'
    keyVaultUrl: '${keyVaultUri}secrets/telegram-bot-token'
    identity: secretIdentity
  }
  {
    name: 'telegram-chat-id'
    keyVaultUrl: '${keyVaultUri}secrets/telegram-chat-id'
    identity: secretIdentity
  }
]

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' existing = {
  name: containerEnvironmentName
}

resource web 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'trading-monitor-web'
  location: location
  tags: {
    application: 'Trading.Monitor'
    component: 'web'
    environment: 'production'
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityResourceId}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        allowInsecure: false
        targetPort: 8080
        transport: 'Auto'
      }
      registries: [
        {
          server: registryLoginServer
          identity: managedIdentityResourceId
        }
      ]
      secrets: commonSecrets
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'TZ', value: 'America/Mexico_City' }
            { name: 'TradingMonitor__DisplayTimeZone', value: 'America/Mexico_City' }
            { name: 'Database__Provider', value: 'SqlServer' }
            { name: 'Database__InitializeOnStartup', value: 'false' }
            { name: 'Database__CreateIfMissing', value: 'false' }
            { name: 'Database__ConnectionString', secretRef: 'database-connection-string' }
            { name: 'AdminAccess__Enabled', value: 'true' }
            { name: 'AdminAccess__Username', secretRef: 'admin-username' }
            { name: 'AdminAccess__Password', secretRef: 'admin-password' }
            { name: 'AdminAccess__SessionHours', value: '8' }
            { name: 'ExchangeExecution__Mode', value: 'Paper' }
            { name: 'ExchangeExecution__AllowLiveOrders', value: 'false' }
            { name: 'Logs__DirectoryPath', value: '/shared-logs' }
            { name: 'TRADING_MONITOR_LOG_DIRECTORY', value: '/shared-logs/web' }
            { name: 'DataProtection__KeysPath', value: '/shared-data/data-protection' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }
              initialDelaySeconds: 2
              periodSeconds: 5
              failureThreshold: 30
              timeoutSeconds: 3
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }
              periodSeconds: 20
              failureThreshold: 3
              timeoutSeconds: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }
              periodSeconds: 10
              failureThreshold: 6
              timeoutSeconds: 5
            }
          ]
          volumeMounts: [
            { volumeName: 'shared-logs', mountPath: '/shared-logs' }
            { volumeName: 'shared-data', mountPath: '/shared-data' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
      volumes: [
        { name: 'shared-logs', storageType: 'AzureFile', storageName: 'sharedlogs' }
        { name: 'shared-data', storageType: 'AzureFile', storageName: 'shareddata' }
      ]
    }
  }
}

resource worker 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'trading-monitor-worker'
  location: location
  tags: {
    application: 'Trading.Monitor'
    component: 'worker'
    environment: 'production'
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${managedIdentityResourceId}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: null
      registries: [
        {
          server: registryLoginServer
          identity: managedIdentityResourceId
        }
      ]
      secrets: commonSecrets
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImage
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'TZ', value: 'America/Mexico_City' }
            { name: 'TradingMonitor__DisplayTimeZone', value: 'America/Mexico_City' }
            { name: 'Database__Provider', value: 'SqlServer' }
            { name: 'Database__InitializeOnStartup', value: 'true' }
            { name: 'Database__CreateIfMissing', value: 'false' }
            { name: 'Database__ConnectionString', secretRef: 'database-connection-string' }
            { name: 'ExchangeExecution__Mode', value: 'Paper' }
            { name: 'ExchangeExecution__AllowLiveOrders', value: 'false' }
            { name: 'OpenAi__Enabled', value: '${openAiEnabled}' }
            { name: 'OPENAI_API_KEY', secretRef: 'openai-api-key' }
            { name: 'MarketSources__AlphaVantageForexEnabled', value: '${alphaVantageEnabled}' }
            { name: 'ALPHA_VANTAGE_API_KEY', secretRef: 'alpha-vantage-api-key' }
            { name: 'News__CryptoPanicEnabled', value: '${cryptoPanicEnabled}' }
            { name: 'CRYPTOPANIC_AUTH_TOKEN', secretRef: 'cryptopanic-auth-token' }
            { name: 'BINANCE_API_KEY', secretRef: 'binance-api-key' }
            { name: 'BINANCE_API_SECRET', secretRef: 'binance-api-secret' }
            { name: 'Notifications__Telegram__BotToken', secretRef: 'telegram-bot-token' }
            { name: 'Notifications__Telegram__ChatId', secretRef: 'telegram-chat-id' }
            { name: 'TRADING_MONITOR_LOG_DIRECTORY', value: '/shared-logs/worker' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }
              initialDelaySeconds: 2
              periodSeconds: 5
              failureThreshold: 30
              timeoutSeconds: 3
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }
              periodSeconds: 20
              failureThreshold: 3
              timeoutSeconds: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }
              periodSeconds: 10
              failureThreshold: 6
              timeoutSeconds: 5
            }
          ]
          volumeMounts: [
            { volumeName: 'shared-logs', mountPath: '/shared-logs' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
      volumes: [
        { name: 'shared-logs', storageType: 'AzureFile', storageName: 'sharedlogs' }
      ]
    }
  }
}

output webName string = web.name
output webFqdn string = web.properties.configuration.ingress.fqdn
output workerName string = worker.name
output requestedCustomDomain string = customDomainName
