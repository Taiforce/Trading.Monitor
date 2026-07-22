namespace Trading.Monitor.Application.Configuration;

public sealed class DatabaseOptions
{
    public string Provider { get; set; } = "SqlServer";

    public string ConnectionString { get; set; } =
        "Server=localhost;Database=TradingMarket;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True";
}
