using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Worker;

public sealed class MarketMonitorWorker(ILogger<MarketMonitorWorker> logger, MarketScanner marketScanner, IServiceScopeFactory scopeFactory, IMarketDataProvider marketDataProvider,
    OpportunityExitService opportunityExitService,
    IEnumerable<INotificationChannel> notificationChannels, IOptionsMonitor<TradingMonitorOptions> monitorOptions, IOptionsMonitor<RiskOptions> riskOptions, IOptionsMonitor<NewsOptions> newsOptions,
    IOptionsMonitor<NotificationOptions> notificationOptions, IOptionsMonitor<ExchangeExecutionOptions> exchangeOptions, IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var exchange = exchangeOptions.CurrentValue;
        logger.LogInformation("Trading monitor started. Exchange execution enabled: {Enabled}. Mode: {Mode}. Live allowed: {LiveAllowed}.", exchange.Enabled, exchange.Mode, exchange.AllowLiveOrders);

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
        var symbols = NormalizeSymbols(monitor.Symbols);
        var intervals = NormalizeIntervals(monitor.Intervals);

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
        var walletRepository = scope.ServiceProvider.GetRequiredService<IWalletRepository>();
        var wallet = await walletRepository.GetSnapshotAsync(cancellationToken);

        if (!WalletSignalPolicy.CanShowSignal(opportunity.Side, opportunity.Symbol, wallet))
        {
            logger.LogInformation("Skipping {Symbol} {SignalType}: wallet has no {Asset} balance for this operation type.", opportunity.Symbol, SignalTypeDescriptor.Label(opportunity.Side),
                WalletSnapshot.ResolveAsset(opportunity.Symbol));
            return;
        }

        var isDuplicate = await repository.HasRecentSimilarSignalAsync(opportunity, duplicateWindow, cancellationToken);

        if (isDuplicate)
        {
            logger.LogInformation("Skipping duplicate {Symbol} {SignalType} signal within {DuplicateWindow}.", opportunity.Symbol, SignalTypeDescriptor.Label(opportunity.Side), duplicateWindow);
            return;
        }

        var learningDecision = await EvaluateLearningAsync(repository, opportunity, cancellationToken);
        if (!learningDecision.Allow)
        {
            logger.LogWarning("Self-learning blocked {Symbol} {SignalType}: {Reason}", opportunity.Symbol, SignalTypeDescriptor.Label(opportunity.Side), learningDecision.Reason);
            return;
        }

        if (learningDecision.ScoreAdjustment > 0)
        {
            opportunity = opportunity with
            {
                Score = Math.Min(100, opportunity.Score + learningDecision.ScoreAdjustment),
                Reasons = opportunity.Reasons.Append(learningDecision.Reason).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        await repository.SaveAsync(opportunity, cancellationToken);
        var executionCapital = Math.Max(0m, exchangeOptions.CurrentValue.MaxCapitalPerTrade);
        var savedOpportunity = await repository.GetByAlertKeyAsync(opportunity.AlertKey, executionCapital, cancellationToken);

        if (savedOpportunity is not null)
        {
            var tradeExecutionService = scope.ServiceProvider.GetRequiredService<ITradeExecutionService>();
            await tradeExecutionService.TryEnterAsync(savedOpportunity, cancellationToken);
        }

        logger.LogInformation("New signal {Symbol} {SignalType} score {Score}. Entry {EntryLower}-{EntryUpper}, pérdida máxima {StopLoss}, ganancia objetivo {TakeProfit1}.", opportunity.Symbol,
            SignalTypeDescriptor.Label(opportunity.Side), opportunity.Score, opportunity.EntryLower, opportunity.EntryUpper, opportunity.StopLoss, opportunity.TakeProfit1);

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
        var tradeExecutionService = scope.ServiceProvider.GetRequiredService<ITradeExecutionService>();
        var openOpportunities = await repository.GetOpenAsync(cancellationToken);
        var risk = riskOptions.CurrentValue;

        foreach (var opportunity in openOpportunities)
        {
            try
            {
                var trackingInterval = ResolveExitTrackingInterval(opportunity);
                var candles = await marketDataProvider.GetCandlesAsync(opportunity.Symbol, trackingInterval, Math.Min(1000, Math.Max(100, monitor.CandleLimit)), cancellationToken);
                var exitCandles = candles;

                if (risk.ManagedProfitExitEnabled
                    && MarketSymbolClassifier.GetMarketKind(opportunity.Symbol) != MarketKind.Forex
                    && opportunityExitService.HasTouchedManagedTarget(opportunity, candles, risk))
                {
                    try
                    {
                        var oneSecondLimit = Math.Min(1000, Math.Max(180, monitor.CandleLimit));
                        var oneSecondCandles = await marketDataProvider.GetCandlesAsync(opportunity.Symbol, "1s", oneSecondLimit, cancellationToken);
                        if (oneSecondCandles.Count >= Math.Max(4, risk.ManagedProfitTrailCandlesAfterTarget + 1))
                        {
                            exitCandles = oneSecondCandles;
                            logger.LogInformation("Opportunity {OpportunityId} reached managed target. Tracking exit with 1s candles.", opportunity.Id);
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Could not switch opportunity {OpportunityId} to 1s exit tracking. Continuing with {Interval}.", opportunity.Id, trackingInterval);
                    }
                }

                var exit = opportunityExitService.ResolveExit(opportunity, exitCandles, risk);

                if (exit is null)
                    continue;

                var breakdown = TradeCostCalculator.Build(opportunity.Side, opportunity.Capital, opportunity.EstimatedQuantity, opportunity.EntryPrice, exit.ExitPrice, risk.EstimatedFeePercentPerSide);
                var gross = breakdown.GrossBenefit;
                var net = breakdown.NetBenefit;
                await repository.UpdateExitAsync(opportunity.Id, exit, gross, net, cancellationToken);
                await tradeExecutionService.TryExitAsync(opportunity, exit, net, cancellationToken);

                logger.LogInformation("Opportunity {Symbol} {SignalType} closed as {Status} at {ExitPrice}. Net PnL for {Capital}: {NetPnL}", opportunity.Symbol,
                    SignalTypeDescriptor.Label(opportunity.Side), exit.Status, exit.ExitPrice, opportunity.Capital, Math.Round(net, 2));

                await SendExitNotificationsAsync(opportunity, exit, net, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not update open opportunity {OpportunityId}.", opportunity.Id);
            }
        }
    }

    private static async Task<SignalLearningDecision> EvaluateLearningAsync(IOpportunityRepository repository, TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        var history = await repository.GetSignalsAsync(1000m, cancellationToken);
        var horizon = ResolveHorizon(opportunity.ObservedAt, opportunity.ExpiresAt);
        var similar = history
            .Where(row => row.Status != OpportunityStatus.Open)
            .Where(row => string.Equals(row.Symbol, opportunity.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Side == opportunity.Side)
            .Where(row => ResolveHorizon(row.ObservedAt, row.ExpiresAt) == horizon)
            .ToArray();

        if (similar.Length < 5)
            return new SignalLearningDecision(true, 0, $"Aprendizaje propio: muestra pequena ({similar.Length}/5) para {horizon}; se permite sin ajuste.");

        var winners = similar.Count(row => row.RealizedNetPnL > 0m);
        var winRate = (decimal)winners / similar.Length * 100m;
        var net = similar.Sum(row => row.RealizedNetPnL ?? 0m);

        if (winRate < 42m && net < 0m)
            return new SignalLearningDecision(false, 0, $"patron {horizon} con {similar.Length} cierres, win rate {winRate:N1}% y neto {net:C2}.");

        if (winRate >= 55m && net > 0m)
            return new SignalLearningDecision(true, 2, $"Aprendizaje propio: patron {horizon} favorable; {similar.Length} cierres, win rate {winRate:N1}%, neto {net:C2}.");

        return new SignalLearningDecision(true, 0, $"Aprendizaje propio: patron {horizon} neutral; {similar.Length} cierres, win rate {winRate:N1}%, neto {net:C2}.");
    }

    private async Task SendExitNotificationsAsync(OpportunityReportRow opportunity, OpportunityExit exit, decimal net, CancellationToken cancellationToken)
    {
        foreach (var channel in notificationChannels)
        {
            if (!IsChannelEnabled(channel.Name))
                continue;

            try
            {
                await channel.SendExitAsync(opportunity, exit, net, cancellationToken);
                logger.LogInformation("Exit signal sent through {Channel}.", channel.Name);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to send exit signal through {Channel}.", channel.Name);
            }
        }
    }

    private static string ResolveExitTrackingInterval(OpportunityReportRow opportunity)
    {
        var minutes = Math.Max(1, (opportunity.ExpiresAt - opportunity.ObservedAt).TotalMinutes);

        return minutes switch
        {
            <= 30 => "1m",
            <= 240 => "5m",
            <= 2880 => "15m",
            <= 10080 => "1h",
            <= 43200 => "4h",
            _ => "1d"
        };
    }

    private static string ResolveHorizon(DateTimeOffset observedAt, DateTimeOffset expiresAt)
    {
        var minutes = Math.Max(1, (expiresAt - observedAt).TotalMinutes);

        return minutes switch
        {
            <= 30 => "Rápida",
            <= 240 => "Intradía",
            <= 2880 => "Swing",
            <= 10080 => "Semanal",
            _ => "Mensual"
        };
    }

    private static Task DelayAsync(TradingMonitorOptions monitor, CancellationToken cancellationToken)
    {
        var seconds = Math.Max(10, monitor.EvaluationIntervalSeconds);
        return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }

    private bool IsChannelEnabled(string channelName)
    {
        var notifications = notificationOptions.CurrentValue;

        return channelName switch
        {
            "console" => notifications.ConsoleEnabled,
            "email" => notifications.Email.Enabled,
            "telegram" => notifications.Telegram.Enabled,
            _ => true
        };
    }

    private static string[] NormalizeSymbols(IEnumerable<string> values)
    {
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] NormalizeIntervals(IEnumerable<string> values)
    {
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    }
}

internal sealed record SignalLearningDecision(bool Allow, int ScoreAdjustment, string Reason);
