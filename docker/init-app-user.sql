-- Creates a least-privilege SQL login for the Web/Worker containers so they
-- never authenticate as `sa`. Run once against the `sqlserver` service by the
-- `sql-init` one-shot container defined in docker-compose.yml.
--
-- $(AppUser) / $(AppPassword) are substituted by sqlcmd's -v variables.

IF DB_ID(N'TradingMarket') IS NULL
BEGIN
    CREATE DATABASE TradingMarket;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(AppUser)')
BEGIN
    CREATE LOGIN [$(AppUser)] WITH PASSWORD = '$(AppPassword)', CHECK_POLICY = OFF;
END
GO

USE TradingMarket;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppUser)')
BEGIN
    CREATE USER [$(AppUser)] FOR LOGIN [$(AppUser)];
END
GO

-- db_owner keeps EF Core migrations/schema-create working for the app user
-- without ever handing out the SQL Server sysadmin (`sa`) account.
ALTER ROLE db_owner ADD MEMBER [$(AppUser)];
GO
