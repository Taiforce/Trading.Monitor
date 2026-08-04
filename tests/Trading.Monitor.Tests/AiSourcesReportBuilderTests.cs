using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class AiSourcesReportBuilderTests
{
    [Fact]
    public void Build_SplitsStatsByOriginKind()
    {
        var rows = new[]
        {
            BuildRow(SignalOriginKind.OwnAi, 3m),
            BuildRow(SignalOriginKind.OwnAi, -1m),
            BuildRow(SignalOriginKind.ExternalAi, 2m),
            BuildRow(SignalOriginKind.Trader, -2m)
        };

        var report = AiSourcesReportBuilder.Build(rows);

        Assert.Equal(2, report.Own.TotalSignals);
        Assert.Equal(1, report.External.TotalSignals);
        Assert.Equal(1, report.Trader.TotalSignals);
        Assert.Equal(2m, report.Net(SignalOriginKind.OwnAi));
    }

    [Fact]
    public void Build_ReportsFewClosedSignalsWhenSampleIsSmall()
    {
        var rows = new[] { BuildRow(SignalOriginKind.OwnAi, 1m) };

        var report = AiSourcesReportBuilder.Build(rows);

        Assert.Contains(report.Own.LearningNotes, note => note.Contains("pocos cierres", StringComparison.OrdinalIgnoreCase));
    }

    private static OpportunityReportRow BuildRow(SignalOriginKind origin, decimal realizedNetPnL)
    {
        return new OpportunityReportRow(Guid.NewGuid(), "BTCUSDT", MarketSide.Long, OpportunityStatus.HitTakeProfit1, 88, DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow.AddMinutes(30),
            DateTimeOffset.UtcNow, 50000m, 49900m, 50100m, 50000m, 49000m, 51000m, 52000m, 50500m, 1000m, 0.02m, 1m, 100m, 200m, -50m, 5m, 50m, 50250m, realizedNetPnL,
            realizedNetPnL / 1000m * 100m, 1000m + realizedNetPnL, 2.5m, "1h", "trend", "", SignalOperationKind.Fixed, origin);
    }
}

internal static class AiSourcesReportTestExtensions
{
    public static decimal Net(this AiSourcesReport report, SignalOriginKind origin)
    {
        return origin switch
        {
            SignalOriginKind.ExternalAi => report.External.NetPnL,
            SignalOriginKind.Trader => report.Trader.NetPnL,
            _ => report.Own.NetPnL
        };
    }
}
