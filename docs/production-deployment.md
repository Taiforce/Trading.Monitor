# Production deployment

Trading Monitor runs in Azure as two independent Container Apps backed by Azure SQL Database.

## Production topology

- `trading-monitor-web`: public HTTPS ingress, one to three replicas, authenticated dashboard.
- `trading-monitor-worker`: no public ingress, exactly one always-on replica.
- Azure SQL Database S0: private endpoint, geo-redundant automated backups.
- Azure Key Vault: database and application secrets.
- Azure Container Registry: immutable Web and Worker images.
- Azure Files: shared operational logs and ASP.NET Core data-protection keys.
- Log Analytics and Application Insights: centralized platform logs and diagnostics.
- GitHub Actions: OIDC-based deployments without a long-lived Azure password.

## First deployment

1. Authenticate with `az login`.
2. Run `./infra/deploy-azure.ps1` from PowerShell.
3. Run `./infra/configure-github-oidc.ps1` to enable secretless GitHub deployments.
4. Create the printed CNAME and TXT records at the DNS provider for `taiforce.com`.
5. Run `./infra/configure-domain.ps1` after DNS propagation.
6. Retrieve the generated dashboard username/password from Key Vault when needed.

API secrets are not copied from `.env.local` by default. This prevents accidentally publishing a key that has already appeared in logs, chat, or shell history. Use the explicit `-IncludeLocalApiSecrets` switch only with newly rotated keys.

Live exchange orders are disabled in production infrastructure. Enabling them requires a separate reviewed configuration change.
