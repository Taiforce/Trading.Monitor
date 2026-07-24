using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Application.Abstractions;

public interface ITraderResearchRepository
{
    Task<TraderResearchReport> GetReportAsync(TraderResearchFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<TraderProfileReportRow>> GetTradersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TraderTradeReportRow>> GetTradesAsync(Guid traderId, DateOnly? desde, DateOnly? hasta, CancellationToken cancellationToken);
}
