namespace Trading.Monitor.Application.Configuration;

public sealed class DatabaseOptions
{
    public string Provider { get; set; } = "SqlServer";

    public bool InitializeOnStartup { get; set; } = true;

    public bool CreateIfMissing { get; set; } = true;

    public string ConnectionString { get; set; } =
        "Server=localhost;Database=TradingMarket;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;MultipleActiveResultSets=True";
}
