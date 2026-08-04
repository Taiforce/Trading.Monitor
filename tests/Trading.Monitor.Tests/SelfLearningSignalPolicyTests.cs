using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public class SelfLearningSignalPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_BlocksOwnSignalWhenPatternHistoryIsUnfavorable()
    {
        var policy = new SelfLearningSignalPolicy();
        var history = BuildClosedRows(SignalOriginKind.OwnAi, winners: 1, losers: 5, netPnLPerLoss: -10m, netPnLPerWin: 5m);
        var repository = new FakeOpportunityRepository(history);
        var opportunity = BuildOpportunity(SignalOriginKind.OwnAi);

        var decision = await policy.EvaluateAsync(repository, opportunity, CancellationToken.None);

        Assert.False(decision.Allow);
    }

    [Fact]
    public async Task EvaluateAsync_BoostsOwnSignalWhenExternalAndTraderSourcesAgree()
    {
        var policy = new SelfLearningSignalPolicy();
        var confirming = new[]
        {
            BuildRow(SignalOriginKind.ExternalAi, MarketSide.Long, OpportunityStatus.Open, null),
            BuildRow(SignalOriginKind.Trader, MarketSide.Long, OpportunityStatus.Open, null)
        };
        var repository = new FakeOpportunityRepository(confirming);
        var opportunity = BuildOpportunity(SignalOriginKind.OwnAi);

        var decision = await policy.EvaluateAsync(repository, opportunity, CancellationToken.None);

        Assert.True(decision.Allow);
        Assert.Equal(4, decision.ScoreAdjustment);
        Assert.Contains("Ajenas", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("Traders", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_DoesNotApplyCrossSourceBoostToNonOwnSignals()
    {
        var policy = new SelfLearningSignalPolicy();
        var confirming = new[] { BuildRow(SignalOriginKind.Trader, MarketSide.Long, OpportunityStatus.Open, null) };
        var repository = new FakeOpportunityRepository(confirming);
        var opportunity = BuildOpportunity(SignalOriginKind.ExternalAi);

        var decision = await policy.EvaluateAsync(repository, opportunity, CancellationToken.None);

        Assert.Equal(0, decision.ScoreAdjustment);
    }

    private static TradingOpportunity BuildOpportunity(SignalOriginKind origin)
    {
        return new TradingOpportunity("BTCUSDT", MarketSide.Long, 88, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(4), 50000m, 49900m, 50100m, 49000m, 51000m, 52000m, 2.5m, ["1h"], [], [], [],
            SignalOperationKind.Fixed, origin);
    }

    private static OpportunityReportRow[] BuildClosedRows(SignalOriginKind origin, int winners, int losers, decimal netPnLPerLoss, decimal netPnLPerWin)
    {
        var rows = new List<OpportunityReportRow>();

        for (var i = 0; i < winners; i++)
            rows.Add(BuildRow(origin, MarketSide.Long, OpportunityStatus.HitTakeProfit1, netPnLPerWin));

        for (var i = 0; i < losers; i++)
            rows.Add(BuildRow(origin, MarketSide.Long, OpportunityStatus.HitStopLoss, netPnLPerLoss));

        return rows.ToArray();
    }

    private static OpportunityReportRow BuildRow(SignalOriginKind origin, MarketSide side, OpportunityStatus status, decimal? realizedNetPnL)
    {
        return new OpportunityReportRow(Guid.NewGuid(), "BTCUSDT", side, status, 88, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(3), realizedNetPnL.HasValue ? DateTimeOffset.UtcNow : null,
            50000m, 49900m, 50100m, 50000m, 49000m, 51000m, 52000m, realizedNetPnL.HasValue ? 51000m : null, 1000m, 0.02m, 1m, 100m, 200m, -50m, 5m, 50m, 50250m, realizedNetPnL,
            realizedNetPnL.HasValue ? realizedNetPnL / 1000m * 100m : null, realizedNetPnL.HasValue ? 1000m + realizedNetPnL : null, 2.5m, "1h", "trend", "", SignalOperationKind.Fixed, origin);
    }

    private sealed class FakeOpportunityRepository(IReadOnlyList<OpportunityReportRow> rows) : IOpportunityRepository
    {
        public Task<bool> HasRecentSimilarSignalAsync(TradingOpportunity opportunity, TimeSpan duplicateWindow, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task SaveAsync(TradingOpportunity opportunity, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<IReadOnlyList<OpportunityReportRow>> GetRecentAsync(int limit, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<IReadOnlyList<OpportunityReportRow>> GetSignalsAsync(decimal capital, CancellationToken cancellationToken) => Task.FromResult(rows);

        public Task<IReadOnlyList<OpportunityReportRow>> GetOpenAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<OpportunityReportRow?> GetByAlertKeyAsync(string alertKey, decimal capital, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<OpportunityReportRow?> GetByIdAsync(Guid id, decimal capital, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<decimal> GetRealizedNetSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task UpdateManagedTargetAsync(Guid id, decimal targetNetPercent, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task UpdateExitAsync(Guid id, OpportunityExit exit, decimal realizedGrossPnL, decimal realizedNetPnL, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<DashboardReport> GetDashboardReportAsync(decimal capital, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
