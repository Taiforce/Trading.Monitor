[CmdletBinding()]
param(
    [string]$Repository = "Taiforce/Trading.Monitor",
    [string]$ResourceGroupName = "rg-trading-monitor-prod",
    [string]$GitHubEnvironment = "production"
)

$ErrorActionPreference = "Stop"

function Assert-LastExitCode([string]$operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$operation failed with exit code $LASTEXITCODE."
    }
}

$account = az account show --query "{subscriptionId:id,tenantId:tenantId}" -o json | ConvertFrom-Json
Assert-LastExitCode "Azure authentication check"

$resourceGroupId = az group show --name $ResourceGroupName --query id -o tsv
Assert-LastExitCode "Resource group lookup"

$applicationName = "github-trading-monitor-production"
$applicationId = az ad app list --display-name $applicationName --query "[0].appId" -o tsv
Assert-LastExitCode "GitHub deployment application lookup"

if ([string]::IsNullOrWhiteSpace($applicationId)) {
    $applicationId = az ad app create --display-name $applicationName --query appId -o tsv
    Assert-LastExitCode "GitHub deployment application creation"
}

$servicePrincipalId = az ad sp list --filter "appId eq '$applicationId'" --query "[0].id" -o tsv
Assert-LastExitCode "GitHub deployment service principal lookup"

if ([string]::IsNullOrWhiteSpace($servicePrincipalId)) {
    $servicePrincipalId = az ad sp create --id $applicationId --query id -o tsv
    Assert-LastExitCode "GitHub deployment service principal creation"
}

$credentialName = "github-$GitHubEnvironment"
$existingCredential = az ad app federated-credential list --id $applicationId --query "[?name=='$credentialName'].name | [0]" -o tsv
Assert-LastExitCode "GitHub federated credential lookup"

if ([string]::IsNullOrWhiteSpace($existingCredential)) {
    $credential = @{
        name = $credentialName
        issuer = "https://token.actions.githubusercontent.com"
        subject = "repo:$Repository`:environment:$GitHubEnvironment"
        description = "Deploy Trading Monitor from the protected GitHub production environment."
        audiences = @("api://AzureADTokenExchange")
    } | ConvertTo-Json -Compress

    az ad app federated-credential create --id $applicationId --parameters $credential --only-show-errors | Out-Null
    Assert-LastExitCode "GitHub federated credential creation"
}

$roleAssignment = az role assignment list `
    --assignee-object-id $servicePrincipalId `
    --scope $resourceGroupId `
    --role Contributor `
    --query "[0].id" `
    --output tsv
Assert-LastExitCode "GitHub deployment role lookup"

if ([string]::IsNullOrWhiteSpace($roleAssignment)) {
    az role assignment create `
        --assignee-object-id $servicePrincipalId `
        --assignee-principal-type ServicePrincipal `
        --scope $resourceGroupId `
        --role Contributor `
        --only-show-errors | Out-Null
    Assert-LastExitCode "GitHub deployment role assignment"
}

$registry = az acr list --resource-group $ResourceGroupName --query "[0].{name:name,server:loginServer}" -o json | ConvertFrom-Json
Assert-LastExitCode "Azure Container Registry lookup"

gh variable set AZURE_CLIENT_ID --repo $Repository --body $applicationId
Assert-LastExitCode "AZURE_CLIENT_ID GitHub variable"
gh variable set AZURE_TENANT_ID --repo $Repository --body $account.tenantId
Assert-LastExitCode "AZURE_TENANT_ID GitHub variable"
gh variable set AZURE_SUBSCRIPTION_ID --repo $Repository --body $account.subscriptionId
Assert-LastExitCode "AZURE_SUBSCRIPTION_ID GitHub variable"
gh variable set AZURE_RESOURCE_GROUP --repo $Repository --body $ResourceGroupName
Assert-LastExitCode "AZURE_RESOURCE_GROUP GitHub variable"
gh variable set AZURE_REGISTRY_NAME --repo $Repository --body $registry.name
Assert-LastExitCode "AZURE_REGISTRY_NAME GitHub variable"
gh variable set AZURE_REGISTRY_SERVER --repo $Repository --body $registry.server
Assert-LastExitCode "AZURE_REGISTRY_SERVER GitHub variable"

Write-Host "GitHub OIDC configured for $Repository and environment $GitHubEnvironment."
