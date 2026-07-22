using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Worker;

public sealed class MarketMonitorWorker(ILogger<MarketMonitorWorker> logger, MarketScanner marketScanner, IServiceScopeFactory scopeFactory, IMarketDataProvider marketDataProvider,
    IEnumerable<INotificationChannel> notificationChannels, IOptionsMonitor<TradingMonitorOptions> monitorOptions, IOptionsMonitor<RiskOptions> riskOptions, IOptionsMonitor<NewsOptions> newsOptions,
    IOptionsMonitor<NotificationOptions> notificationOptions, IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Trading monitor started. This service only produces proposals; it never places real orders.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var monitor = monitorOptions.CurrentValue;

            if (!monitor.Enabled)
            {
                logger.LogWarning("Trading monitor is disabled in configuration.");

                if (monitor.RunOnce)
                {
                    applicationLifetime.StopApplication();
                    break;
                }

                await DelayAsync(monitor, stoppingToken);
                continue;
            }

            await ScanOnceAsync(stoppingToken);

            if (monitor.RunOnce)
            {
                logger.LogInformation("RunOnce is enabled; stopping after one scan.");
                applicationLifetime.StopApplication();
                break;
            }

            await DelayAsync(monitor, stoppingToken);
        }
    }

    private async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        var monitor = monitorOptions.CurrentValue;
        var risk = riskOptions.CurrentValue;
        var news = newsOptions.CurrentValue;
        var symbols = Normalize(monitor.Symbols);
        var intervals = Normalize(monitor.Intervals);

        logger.LogInformation("Scanning {Symbols} across {Intervals}. Minimum score: {MinimumScore}", string.Join(", ", symbols), string.Join(", ", intervals), monitor.MinimumScore);

        var result = await marketScanner.ScanAsync(monitor, risk, news, cancellationToken);

        foreach (var error in result.Errors)
            logger.LogWarning("{ScanError}", error);

        foreach (var opportunity in result.Opportunities.OrderByDescending(opportunity => opportunity.Score))
            await ProcessOpportunityAsync(opportunity, monitor, cancellationToken);

        if (result.Opportunities.Count == 0)
            logger.LogInformation("No valid opportunities found in this scan.");

        await UpdateOpenOpportunitiesAsync(monitor, cancellationToken);
    }

    private async Task ProcessOpportunityAsync(TradingOpportunity opportunity, TradingMonitorOptions monitor, CancellationToken cancellationToken)
    {
        var duplicateWindow = TimeSpan.FromMinutes(Math.Max(1, monitor.DuplicateWindowMinutes));
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOpportunityRepository>();
        var isDuplicate = await repository.HasRecentSimilarSignalAsync(opportunity, duplicateWindow, cancellationToken);

        if (isDuplicate)
        {
            logger.LogInformation("Skipping duplicate {Symbol} {Side} signal within {DuplicateWindow}.", opportunity.Symbol, opportunity.Side, duplicateWindow);
            return;
        }

        await repository.SaveAsync(opportunity, cancellationToken);

        logger.LogInformation("New signal {Symbol} {Side} score {Score}. Entry {EntryLower}-{EntryUpper}, stop {StopLoss}, TP1 {TakeProfit1}.", opportunity.Symbol, opportunity.Side, opportunity.Score,
            opportunity.EntryLower, opportunity.EntryUpper, opportunity.StopLoss, opportunity.TakeProfit1);

        foreach (var channel in notificationChannels)
        {
            if (channel.Name == "console" && !notificationOptions.CurrentValue.ConsoleEnabled)
                continue;

            if (channel.Name == "email" && !notificationOptions.CurrentValue.Email.Enabled)
                continue;

            if (channel.Name == "telegram" && !notificationOptions.CurrentValue.Telegram.Enabled)
                continue;

            try
            {
                await channel.SendAsync(opportunity, cancellationToken);
                logger.LogInformation("Signal sent through {Channel}.", channel.Name);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to send signal through {Channel}.", channel.Name);
            }
        }
    }

    private async Task UpdateOpenOpportunitiesAsync(TradingMonitorOptions monitor, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOpportunityRepository>();
        var openOpportunities = await repository.GetOpenAsync(cancellationToken);

        foreach (var opportunity in openOpportunities)
        {
            try
            {
                var candles = await marketDataProvider.GetCandlesAsync(opportunity.Symbol, monitor.TriggerInterval, Math.Min(1000, Math.Max(100, monitor.CandleLimit)), cancellationToken);

                var exit = ResolveExit(opportunity, candles);

                if (exit is null)
                    continue;

                var gross = OpportunityProjectionService.CalculateGrossPnL(opportunity.Side, opportunity.EntryPrice, exit.ExitPrice, opportunity.EstimatedQuantity);

                var net = gross - opportunity.EstimatedFees;
                await repository.UpdateExitAsync(opportunity.Id, exit, gross, net, cancellationToken);

                logger.LogInformation("Opportunity {Symbol} {Side} closed as {Status} at {ExitPrice}. Net PnL for {Capital}: {NetPnL}", opportunity.Symbol, opportunity.Side, exit.Status, exit.ExitPrice,
                    opportunity.Capital, Math.Round(net, 2));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not update open opportunity {OpportunityId}.", opportunity.Id);
            }
        }
    }

    private static OpportunityExit? ResolveExit(OpportunityReportRow opportunity, IReadOnlyList<MarketCandle> candles)
    {
        var relevantCandles = candles.Where(candle => candle.CloseTime > opportunity.ObservedAt).OrderBy(candle => candle.CloseTime).ToArray();

        foreach (var candle in relevantCandles)
        {
            if (opportunity.Side == MarketSide.Long)
            {
                if (candle.Low <= opportunity.StopLoss)
                {
                    return new OpportunityExit(OpportunityStatus.HitStopLoss, candle.CloseTime, opportunity.StopLoss, "Stop loss touched before target in tracked candle sequence.");
                }

                if (candle.High >= opportunity.TakeProfit2)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit2, candle.CloseTime, opportunity.TakeProfit2, "Take profit 2 touched.");

                if (candle.High >= opportunity.TakeProfit1)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit1, candle.CloseTime, opportunity.TakeProfit1, "Take profit 1 touched.");
            }
            else
            {
                if (candle.High >= opportunity.StopLoss)
                {
                    return new OpportunityExit(OpportunityStatus.HitStopLoss, candle.CloseTime, opportunity.StopLoss, "Stop loss touched before target in tracked candle sequence.");
                }

                if (candle.Low <= opportunity.TakeProfit2)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit2, candle.CloseTime, opportunity.TakeProfit2, "Take profit 2 touched.");

                if (candle.Low <= opportunity.TakeProfit1)
                    return new OpportunityExit(OpportunityStatus.HitTakeProfit1, candle.CloseTime, opportunity.TakeProfit1, "Take profit 1 touched.");
            }
        }

        if (DateTimeOffset.UtcNow > opportunity.ExpiresAt && relevantCandles.Length > 0)
        {
            var last = relevantCandles[^1];
            return new OpportunityExit(OpportunityStatus.Expired, last.CloseTime, last.Close, "Signal expired before hitting stop or target.");
        }

        return null;
    }

    private static Task DelayAsync(TradingMonitorOptions monitor, CancellationToken cancellationToken)
    {
        var seconds = Math.Max(10, monitor.EvaluationIntervalSeconds);
        return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }

    private static string[] Normalize(IEnumerable<string> values)
    {
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
