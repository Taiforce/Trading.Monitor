targetScope = 'subscription'

@description('Azure region for all Trading Monitor resources.')
param location string = 'eastus2'

@description('Resource group name.')
param resourceGroupName string = 'rg-trading-monitor-prod'

@description('Short resource-name prefix.')
param prefix string = 'tradingmonitor'

@description('Microsoft Entra object ID of the owner who deploys and manages production secrets.')
param deployerObjectId string

@description('SQL logical-server administrator login.')
param sqlAdminLogin string = 'tradingmonitoradmin'

@secure()
@description('SQL logical-server administrator password.')
param sqlAdminPassword string

@secure()
@description('Trading Monitor web administrator username.')
param adminUsername string

@secure()
@description('Trading Monitor web administrator password.')
param adminPassword string

@secure()
param openAiApiKey string = ''

@secure()
param binanceApiKey string = ''

@secure()
param binanceApiSecret string = ''

@secure()
param alphaVantageApiKey string = ''

@secure()
param cryptoPanicAuthToken string = ''

@secure()
param telegramBotToken string = ''

@secure()
param telegramChatId string = ''

resource resourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: location
  tags: {
    application: 'Trading.Monitor'
    environment: 'production'
    managedBy: 'Bicep'
  }
}

module core './core.bicep' = {
  name: 'trading-monitor-core'
  scope: resourceGroup
  params: {
    location: location
    prefix: prefix
    deployerObjectId: deployerObjectId
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    adminUsername: adminUsername
    adminPassword: adminPassword
    openAiApiKey: openAiApiKey
    binanceApiKey: binanceApiKey
    binanceApiSecret: binanceApiSecret
    alphaVantageApiKey: alphaVantageApiKey
    cryptoPanicAuthToken: cryptoPanicAuthToken
    telegramBotToken: telegramBotToken
    telegramChatId: telegramChatId
  }
}

output resourceGroupName string = resourceGroup.name
output registryName string = core.outputs.registryName
output registryLoginServer string = core.outputs.registryLoginServer
output containerEnvironmentName string = core.outputs.containerEnvironmentName
output managedIdentityResourceId string = core.outputs.managedIdentityResourceId
output keyVaultName string = core.outputs.keyVaultName
output keyVaultUri string = core.outputs.keyVaultUri
output sqlServerName string = core.outputs.sqlServerName
output sqlDatabaseName string = core.outputs.sqlDatabaseName
output logAnalyticsWorkspaceName string = core.outputs.logAnalyticsWorkspaceName
