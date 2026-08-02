[CmdletBinding()]
param(
    [string]$ResourceGroupName = "rg-trading-monitor-prod",
    [string]$ContainerAppName = "trading-monitor-web",
    [string]$ContainerEnvironmentName,
    [string]$DomainName = "tradingmonitor.taiforce.com"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ContainerEnvironmentName)) {
    $ContainerEnvironmentName = az containerapp show `
        --resource-group $ResourceGroupName `
        --name $ContainerAppName `
        --query "properties.environmentId" `
        --output tsv
    if ($LASTEXITCODE -ne 0) { throw "Could not resolve the Container Apps environment." }
    $ContainerEnvironmentName = ($ContainerEnvironmentName -split "/")[-1]
}

$targetFqdn = az containerapp show `
    --resource-group $ResourceGroupName `
    --name $ContainerAppName `
    --query "properties.configuration.ingress.fqdn" `
    --output tsv
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the Container App FQDN." }

$cname = Resolve-DnsName -Name $DomainName -Type CNAME -ErrorAction SilentlyContinue
if (-not $cname -or $cname.NameHost.TrimEnd(".") -ne $targetFqdn.TrimEnd(".")) {
    throw "DNS is not ready. Create CNAME $DomainName -> $targetFqdn and wait for propagation."
}

az containerapp hostname add `
    --resource-group $ResourceGroupName `
    --name $ContainerAppName `
    --hostname $DomainName `
    --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "The hostname could not be added." }

az containerapp hostname bind `
    --resource-group $ResourceGroupName `
    --name $ContainerAppName `
    --environment $ContainerEnvironmentName `
    --hostname $DomainName `
    --validation-method CNAME `
    --only-show-errors
if ($LASTEXITCODE -ne 0) { throw "The managed certificate could not be created or bound." }

Write-Host "Custom domain configured: https://$DomainName"
