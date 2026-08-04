# AGENTS.md

## Cursor Cloud specific instructions

### What this project is
Trading Monitor is a .NET 10 solution (`Trading.Monitor.sln`) with three runnable pieces:
- **Web** (`src/Trading.Monitor.Web`): the dashboard (Razor Pages + minimal APIs), listens on port `5088` in dev.
- **Worker** (`src/Trading.Monitor.Worker`): a background hosted service that scans markets and generates trade signals.
- **SQL Server**: the only supported database. There is **no** Sqlite/InMemory fallback — `Database:Provider` must be `SqlServer` or DI throws at startup.

Standard build/test/run commands are already documented in `README.md` and `CONTRIBUTING.md`; this section only records the non-obvious cloud-environment specifics.

### Prerequisites already installed in the VM snapshot
- **.NET 10 SDK 10.0.302** (matches `global.json`), on `PATH` at `/usr/local/bin/dotnet`.
- **Docker** (used only to run the SQL Server container). The update script does NOT install these.

### Required startup steps (NOT in the update script — services must not be auto-started there)
The database is mandatory: Web and Worker run EF Core migrations on startup (`Database:InitializeOnStartup=true`) and **crash if SQL Server is unreachable**. Before running either app:

1. Start the Docker daemon if it is not running:
   ```bash
   sudo pgrep dockerd >/dev/null || (sudo nohup dockerd > /tmp/dockerd.log 2>&1 &)
   ```
2. Start (or create) the SQL Server 2022 container on `127.0.0.1:14333`:
   ```bash
   sudo docker start trading-monitor-sqlserver 2>/dev/null || \
   sudo docker run -d --name trading-monitor-sqlserver \
     -e "ACCEPT_EULA=Y" -e "MSSQL_PID=Developer" -e "MSSQL_SA_PASSWORD=Dev_Str0ng!Pass1" \
     -p 127.0.0.1:14333:1433 mcr.microsoft.com/mssql/server:2022-latest
   ```
   Dev SA password: `Dev_Str0ng!Pass1`. It can take ~10s to accept connections after start.

### Dev connection string
Point the apps at the container by exporting this before `dotnet run` (the committed default connection string uses Windows auth on `localhost`, which does not apply here):
```
Database__ConnectionString=Server=localhost,14333;Database=TradingMarket;User Id=sa;Password=Dev_Str0ng!Pass1;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True
```

### Running the apps (development)
```bash
# Worker: in Development it uses RunOnce=true (one market scan, then exits). Creates DB + tables + signals.
ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development OpenAi__Enabled=false \
  Database__ConnectionString="Server=localhost,14333;Database=TradingMarket;User Id=sa;Password=Dev_Str0ng!Pass1;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True" \
  dotnet run --project src/Trading.Monitor.Worker --no-build

# Web dashboard on http://localhost:5088 (runs continuously)
ASPNETCORE_ENVIRONMENT=Development OpenAi__Enabled=false \
  Database__ConnectionString="Server=localhost,14333;Database=TradingMarket;User Id=sa;Password=Dev_Str0ng!Pass1;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True" \
  dotnet run --project src/Trading.Monitor.Web --no-build --urls http://0.0.0.0:5088
```
Health checks: `GET /health/live` (process) and `GET /health/ready` (DB connectivity). Live data API: `GET /api/operaciones-vivas?capital=1000`.

### Non-obvious gotchas
- **AdminAccess**: In `Development` it is disabled (no login). The Production `appsettings.json` enables it and the Web app **throws at startup if `AdminAccess:Username`/`Password` are empty**. Keep `ASPNETCORE_ENVIRONMENT=Development` for local dev to avoid the login wall and that crash.
- **Binance returns HTTP 451 from this VM** (geo-blocked). This is expected and not a setup failure: the app logs it in the `data_sources` / "Conexiones" view and falls back to Coinbase/Kraken/Yahoo Finance, so historical backfill and signal generation still work.
- **OpenAI**: set `OpenAi__Enabled=false` in dev (there is no API key), otherwise the worker attempts external OpenAI calls.
- **Browser libs** (`src/Trading.Monitor.Web/wwwroot/lib`) are committed to the repo, so `libman restore` is only needed when `libman.json` changes — it is intentionally not part of routine startup.
- **Docker in this VM** uses the `fuse-overlayfs` storage driver with the containerd snapshotter disabled (see `/etc/docker/daemon.json`); the kernel does not support full overlay2/nftables.
