using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class TradingMonitorDbContextFactory : IDesignTimeDbContextFactory<TradingMonitorDbContext>
{
    public TradingMonitorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString")
                               ?? "Server=localhost;Database=TradingMarket;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True";

        var options = new DbContextOptionsBuilder<TradingMonitorDbContext>()
            .UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure())
            .Options;

        return new TradingMonitorDbContext(options);
    }
}
