[CmdletBinding()]
param(
    [string]$Location = "eastus2",
    [string]$ResourceGroupName = "rg-trading-monitor-prod",
    [string]$AdminUsername = "Taiforce",
    [string]$DomainName = "tradingmonitor.taiforce.com",
    [switch]$IncludeLocalApiSecrets
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$compiledDirectory = Join-Path $repositoryRoot "work\bicep"
$mainTemplate = Join-Path $compiledDirectory "main.json"
$appsTemplate = Join-Path $compiledDirectory "apps.json"

function Assert-LastExitCode([string]$operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE."
    }
}

function New-StrongSecret {
    $bytes = [byte[]]::new(36)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $random = [Convert]::ToBase64String($bytes).Replace("/", "x").Replace("+", "Y").Replace("=", "z")
    return "Tm!9aA$random"
}

function Read-LocalEnvironment {
    $values = @{}
    $path = Join-Path $repositoryRoot ".env.local"
    if (-not (Test-Path -LiteralPath $path)) {
        return $values
    }

    foreach ($line in Get-Content -LiteralPath $path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separator = $trimmed.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $values[$trimmed.Substring(0, $separator).Trim()] = $trimmed.Substring($separator + 1).Trim()
    }

    return $values
}

Push-Location $repositoryRoot
try {
    $account = az account show --query "{id:id,name:name}" -o json | ConvertFrom-Json
    Assert-LastExitCode "Azure authentication check"
    $deployerObjectId = az ad signed-in-user show --query id -o tsv
    Assert-LastExitCode "Microsoft Entra user lookup"
    Write-Host "Using Azure subscription: $($account.name)"

    New-Item -ItemType Directory -Force -Path $compiledDirectory | Out-Null
    bicep build (Join-Path $PSScriptRoot "main.bicep") --outfile $mainTemplate
    Assert-LastExitCode "Core Bicep compilation"
    bicep build (Join-Path $PSScriptRoot "apps.bicep") --outfile $appsTemplate
    Assert-LastExitCode "Application Bicep compilation"

    $localEnvironment = Read-LocalEnvironment
    $sqlPassword = New-StrongSecret
    $adminPassword = New-StrongSecret

    $openAiApiKey = ""
    $binanceApiKey = ""
    $binanceApiSecret = ""
    $alphaVantageApiKey = ""
    $cryptoPanicAuthToken = ""
    $telegramBotToken = ""
    $telegramChatId = ""

    if ($IncludeLocalApiSecrets) {
        $openAiApiKey = $localEnvironment["OPENAI_API_KEY"]
        $binanceApiKey = $localEnvironment["BINANCE_API_KEY"]
        $binanceApiSecret = $localEnvironment["BINANCE_API_SECRET"]
        $alphaVantageApiKey = $localEnvironment["ALPHA_VANTAGE_API_KEY"]
        $cryptoPanicAuthToken = $localEnvironment["CRYPTOPANIC_AUTH_TOKEN"]
        $telegramBotToken = $localEnvironment["TELEGRAM_BOT_TOKEN"]
        $telegramChatId = $localEnvironment["TELEGRAM_CHAT_ID"]
    }

    $deploymentName = "trading-monitor-core-$(Get-Date -Format 'yyyyMMddHHmmss')"
    $coreJson = az deployment sub create `
        --name $deploymentName `
        --location $Location `
        --template-file $mainTemplate `
        --parameters `
            location=$Location `
            resourceGroupName=$ResourceGroupName `
            deployerObjectId=$deployerObjectId `
            sqlAdminPassword=$sqlPassword `
            adminUsername=$AdminUsername `
            adminPassword=$adminPassword `
            openAiApiKey=$openAiApiKey `
            binanceApiKey=$binanceApiKey `
            binanceApiSecret=$binanceApiSecret `
            alphaVantageApiKey=$alphaVantageApiKey `
            cryptoPanicAuthToken=$cryptoPanicAuthToken `
            telegramBotToken=$telegramBotToken `
            telegramChatId=$telegramChatId `
        --query properties.outputs `
        --output json `
        --only-show-errors
    Assert-LastExitCode "Azure core deployment"
    $core = $coreJson | ConvertFrom-Json

    $registryName = $core.registryName.value
    $registryLoginServer = $core.registryLoginServer.value
    $containerEnvironmentName = $core.containerEnvironmentName.value
    $managedIdentityResourceId = $core.managedIdentityResourceId.value
    $keyVaultUri = $core.keyVaultUri.value
    $imageTag = "$(git rev-parse --short=12 HEAD)-$(Get-Date -Format 'yyyyMMddHHmmss')"

    az acr build --registry $registryName --image "trading-monitor-web:$imageTag" --file docker/web.Dockerfile . --only-show-errors
    Assert-LastExitCode "Web image build"
    az acr build --registry $registryName --image "trading-monitor-worker:$imageTag" --file docker/worker.Dockerfile . --only-show-errors
    Assert-LastExitCode "Worker image build"

    $openAiEnabled = $IncludeLocalApiSecrets -and -not [string]::IsNullOrWhiteSpace($openAiApiKey)
    $alphaVantageEnabled = $IncludeLocalApiSecrets -and -not [string]::IsNullOrWhiteSpace($alphaVantageApiKey)
    $cryptoPanicEnabled = $IncludeLocalApiSecrets -and -not [string]::IsNullOrWhiteSpace($cryptoPanicAuthToken)

    $appsJson = az deployment group create `
        --name "trading-monitor-apps-$imageTag" `
        --resource-group $ResourceGroupName `
        --template-file $appsTemplate `
        --parameters `
            containerEnvironmentName=$containerEnvironmentName `
            managedIdentityResourceId=$managedIdentityResourceId `
            registryLoginServer=$registryLoginServer `
            keyVaultUri=$keyVaultUri `
            webImage="$registryLoginServer/trading-monitor-web:$imageTag" `
            workerImage="$registryLoginServer/trading-monitor-worker:$imageTag" `
            openAiEnabled=$openAiEnabled `
            alphaVantageEnabled=$alphaVantageEnabled `
            cryptoPanicEnabled=$cryptoPanicEnabled `
            customDomainName=$DomainName `
        --query properties.outputs `
        --output json `
        --only-show-errors
    Assert-LastExitCode "Azure application deployment"
    $apps = $appsJson | ConvertFrom-Json

    $verificationId = az containerapp show-custom-domain-verification-id --query customDomainVerificationId -o tsv
    Assert-LastExitCode "Custom-domain verification lookup"

    Write-Host ""
    Write-Host "Azure deployment completed."
    Write-Host "Web URL: https://$($apps.webFqdn.value)"
    Write-Host "Production credentials are stored in Key Vault: $($core.keyVaultName.value)"
    Write-Host "DNS CNAME: $DomainName -> $($apps.webFqdn.value)"
    Write-Host "DNS TXT: asuid.$($DomainName.Split('.')[0]) -> $verificationId"
    Write-Host "After DNS is visible, run infra/configure-domain.ps1."
}
finally {
    Pop-Location
}
