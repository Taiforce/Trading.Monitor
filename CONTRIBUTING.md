# Contributing

## Local setup

1. Install the .NET 10 SDK and Docker Desktop.
2. Run `dotnet tool restore` and `dotnet restore --locked-mode`.
3. Copy `.env.example` to `.env`, set a strong local SQL Server password, and keep secrets in `.env.local`.
4. Run `dotnet build --configuration Release` and `dotnet test --configuration Release`.
5. Restore browser libraries with `dotnet tool run libman restore` when `libman.json` changes.

Create focused branches, keep credentials out of Git, add tests for behavior changes, and confirm that the Release build has no warnings before opening a pull request.
