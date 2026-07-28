using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Exchange;

public sealed class SafeTradeExecutionService(
    IOptionsMonitor<ExchangeExecutionOptions> optionsMonitor,
    ITradeExecutionRepository executionRepository,
    IOpportunityRepository opportunityRepository,
    IWalletRepository walletRepository,
    IExchangeExecutionClient exchangeClient,
    ILogger<SafeTradeExecutionService> logger) : ITradeExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task TryEnterAsync(OpportunityReportRow opportunity, CancellationToken cancellationToken)
    {
        var existingEntry = await executionRepository.GetLatestEntryAsync(opportunity.Id, cancellationToken);
        if (existingEntry is not null)
            return;

        var options = optionsMonitor.CurrentValue;
        var mode = ResolveMode(options.Mode);
        var action = opportunity.Side == MarketSide.Long ? TradeExecutionAction.BuyToOpen : TradeExecutionAction.SellToOpen;
        var requestedCapital = ResolveCapital(opportunity, options);
        var requestedQuantity = EstimateQuantity(requestedCapital, opportunity.EntryPrice);
        var clientOrderId = BuildClientOrderId("tm-entry", opportunity.Id);
        var requestJson = Serialize(new
        {
            opportunity.Id,
            opportunity.Symbol,
            opportunity.Side,
            Action = action,
            Mode = mode,
            opportunity.Score,
            RequestedCapital = requestedCapital,
            RequestedQuantity = requestedQuantity,
            opportunity.EntryPrice,
            opportunity.StopLoss,
            opportunity.TakeProfit1,
            opportunity.TakeProfit2
        });

        var decision = await ValidateEntryAsync(opportunity, options, mode, requestedCapital, cancellationToken);
        if (!decision.Allowed)
        {
            await SaveAsync(opportunity, action, mode, decision.Status, requestedCapital, requestedQuantity, null, null, opportunity.EntryPrice, clientOrderId, "", decision.Reason, decision.Message, requestJson, "{}", cancellationToken);
            return;
        }

        if (mode == TradeExecutionMode.Paper)
        {
            await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Simulated, requestedCapital, requestedQuantity, requestedQuantity, requestedCapital, opportunity.EntryPrice, clientOrderId, "",
                "paper-entry", "Entrada simulada. No se envio orden real al exchange.", requestJson, "{}", cancellationToken);
            return;
        }

        if (opportunity.Side == MarketSide.Short)
        {
            await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Blocked, requestedCapital, requestedQuantity, null, null, opportunity.EntryPrice, clientOrderId, "",
                "spot-short-blocked", "Binance Spot no abre ventas en corto reales desde este conector. Usa Paper o conecta Margin/Futures con permisos separados.", requestJson, "{}", cancellationToken);
            return;
        }

        try
        {
            var rules = await exchangeClient.GetSymbolRulesAsync(opportunity.Symbol, cancellationToken);
            if (rules.MinNotional > 0m && requestedCapital < rules.MinNotional)
            {
                await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Blocked, requestedCapital, requestedQuantity, null, null, opportunity.EntryPrice, clientOrderId, "",
                    "min-notional", $"Capital {requestedCapital:N2} menor al minimo permitido por Binance para {opportunity.Symbol}: {rules.MinNotional:N2}.", requestJson, "{}", cancellationToken);
                return;
            }

            var result = await exchangeClient.PlaceMarketBuyAsync(opportunity.Symbol, requestedCapital, clientOrderId, ShouldUseTestEndpoint(options, mode), cancellationToken);
            var executedQuantity = result.ExecutedQuantity ?? requestedQuantity;
            var executedQuote = result.ExecutedQuote ?? requestedCapital;
            var price = result.Price ?? opportunity.EntryPrice;

            await SaveAsync(opportunity, action, mode, result.Status, requestedCapital, requestedQuantity, executedQuantity, executedQuote, price, clientOrderId, result.ExchangeOrderId,
                result.Status == TradeExecutionStatus.Failed ? "exchange-failed" : "exchange-entry", result.Message, requestJson, result.RawResponse, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Automatic entry execution failed for {Symbol}.", opportunity.Symbol);
            await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Failed, requestedCapital, requestedQuantity, null, null, opportunity.EntryPrice, clientOrderId, "",
                "exchange-exception", exception.Message, requestJson, "{}", cancellationToken);
        }
    }

    public async Task TryExitAsync(OpportunityReportRow opportunity, OpportunityExit exit, decimal realizedNetPnL, CancellationToken cancellationToken)
    {
        var entry = await executionRepository.GetLatestEntryAsync(opportunity.Id, cancellationToken);
        var options = optionsMonitor.CurrentValue;
        var mode = entry?.Mode ?? ResolveMode(options.Mode);
        var action = opportunity.Side == MarketSide.Long ? TradeExecutionAction.SellToClose : TradeExecutionAction.BuyToClose;
        var requestedCapital = entry?.RequestedCapital > 0m ? entry.RequestedCapital : ResolveCapital(opportunity, options);
        var requestedQuantity = entry?.ExecutedQuantity ?? entry?.RequestedQuantity ?? EstimateQuantity(requestedCapital, opportunity.EntryPrice);
        var clientOrderId = BuildClientOrderId("tm-exit", opportunity.Id);
        var requestJson = Serialize(new
        {
            opportunity.Id,
            opportunity.Symbol,
            opportunity.Side,
            Action = action,
            Mode = mode,
            RequestedCapital = requestedCapital,
            RequestedQuantity = requestedQuantity,
            exit.ExitTime,
            exit.ExitPrice,
            exit.Status,
            RealizedNetPnL = realizedNetPnL
        });

        var decision = ValidateExit(opportunity, entry, options, mode);
        if (!decision.Allowed)
        {
            await SaveAsync(opportunity, action, mode, decision.Status, requestedCapital, requestedQuantity, null, null, exit.ExitPrice, clientOrderId, "", decision.Reason, decision.Message, requestJson, "{}", cancellationToken);
            return;
        }

        if (mode == TradeExecutionMode.Paper)
        {
            await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Simulated, requestedCapital, requestedQuantity, requestedQuantity, requestedQuantity * exit.ExitPrice, exit.ExitPrice, clientOrderId, "",
                "paper-exit", $"Salida simulada. Resultado estimado despues de comisiones: {realizedNetPnL.ToString("C2", CultureInfo.CurrentCulture)}.", requestJson, "{}", cancellationToken);
            return;
        }

        if (opportunity.Side == MarketSide.Short)
        {
            await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Blocked, requestedCapital, requestedQuantity, null, null, exit.ExitPrice, clientOrderId, "",
                "spot-short-blocked", "Binance Spot no cierra ventas en corto reales desde este conector. Usa Paper o conecta Margin/Futures con permisos separados.", requestJson, "{}", cancellationToken);
            return;
        }

        try
        {
            var rules = await exchangeClient.GetSymbolRulesAsync(opportunity.Symbol, cancellationToken);
            var quantity = RoundDown(requestedQuantity, rules.StepSize);

            if (quantity <= 0m || (rules.MinQuantity > 0m && quantity < rules.MinQuantity))
            {
                await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Blocked, requestedCapital, requestedQuantity, null, null, exit.ExitPrice, clientOrderId, "",
                    "min-quantity", $"Cantidad {quantity:N8} menor al minimo permitido por Binance para {opportunity.Symbol}.", requestJson, "{}", cancellationToken);
                return;
            }

            if (mode == TradeExecutionMode.Live)
            {
                var balance = await exchangeClient.GetBalanceAsync(ResolveBaseAsset(opportunity.Symbol), cancellationToken);

                if (balance is null || balance.Free <= 0m)
                {
                    await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Blocked, requestedCapital, requestedQuantity, null, null, exit.ExitPrice, clientOrderId, "",
                        "no-balance", $"No hay saldo disponible de {ResolveBaseAsset(opportunity.Symbol)} para cerrar la posicion.", requestJson, "{}", cancellationToken);
                    return;
                }

                quantity = Math.Min(quantity, RoundDown(balance.Free, rules.StepSize));
            }

            var result = await exchangeClient.PlaceMarketSellAsync(opportunity.Symbol, quantity, clientOrderId, ShouldUseTestEndpoint(options, mode), cancellationToken);
            var executedQuantity = result.ExecutedQuantity ?? quantity;
            var executedQuote = result.ExecutedQuote ?? executedQuantity * exit.ExitPrice;
            var price = result.Price ?? exit.ExitPrice;

            await SaveAsync(opportunity, action, mode, result.Status, requestedCapital, quantity, executedQuantity, executedQuote, price, clientOrderId, result.ExchangeOrderId,
                result.Status == TradeExecutionStatus.Failed ? "exchange-failed" : "exchange-exit", result.Message, requestJson, result.RawResponse, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Automatic exit execution failed for {Symbol}.", opportunity.Symbol);
            await SaveAsync(opportunity, action, mode, TradeExecutionStatus.Failed, requestedCapital, requestedQuantity, null, null, exit.ExitPrice, clientOrderId, "",
                "exchange-exception", exception.Message, requestJson, "{}", cancellationToken);
        }
    }

    private async Task<ExecutionDecision> ValidateEntryAsync(OpportunityReportRow opportunity, ExchangeExecutionOptions options, TradeExecutionMode mode, decimal requestedCapital, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "execution-disabled", "Ejecución automática desactivada. La señal solo queda como propuesta.");

        var wallet = await walletRepository.GetSnapshotAsync(cancellationToken);

        if (!wallet.AutoTradingEnabled)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "wallet-auto-disabled", "Automático desactivado en Wallet. La señal queda como propuesta.");

        if (!wallet.CanAutoTrade(opportunity.Symbol))
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "wallet-symbol-auto-disabled", $"Automático desactivado en Wallet para {opportunity.Symbol}.");

        if (!WalletSignalPolicy.CanShowSignal(opportunity, wallet))
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "wallet-no-base-asset", $"Wallet no tiene {ResolveBaseAsset(opportunity.Symbol)} disponible; se omite vende alto - compra bajo.");

        if (opportunity.Side == MarketSide.Long && wallet.CashCapital <= 0m)
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "wallet-cash-zero", "Wallet no tiene capital disponible para comprar.");

        if (opportunity.Side == MarketSide.Long && requestedCapital > wallet.CashCapital)
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "wallet-cash-too-low", $"Capital automático {requestedCapital:N2} mayor al capital disponible en Wallet {wallet.CashCapital:N2}.");

        if (!options.EnableEntryOrders)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "entries-disabled", "Entradas automaticas desactivadas.");

        if (!IsSymbolAllowed(opportunity.Symbol, options))
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "symbol-not-allowed", $"{opportunity.Symbol} no esta en la lista de simbolos permitidos.");

        if (opportunity.Score < Math.Clamp(options.MinimumScoreToExecute, 1, 100))
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "score-too-low", $"Score {opportunity.Score}/100 menor al minimo automatico {options.MinimumScoreToExecute}/100.");

        if (opportunity.Capital > 0m)
        {
            var expectedPercent = opportunity.NetProfitAtTakeProfit1 / opportunity.Capital * 100m;
            if (expectedPercent < options.MinimumExpectedNetProfitPercentAfterCosts)
            {
                return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "profit-after-costs-too-low",
                    $"Ganancia esperada neta {expectedPercent:N2}% queda por debajo del minimo {options.MinimumExpectedNetProfitPercentAfterCosts:N2}% despues de comisiones.");
            }
        }

        if (!IsPriceInsideEntryBand(opportunity, options.MaxSlippagePercent))
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "price-outside-entry", "El precio observado ya se alejo del rango de entrada permitido.");

        if (opportunity.Side == MarketSide.Short && !options.AllowShortSelling)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "short-disabled", "Senal vende alto - compra bajo omitida: no esta habilitada la venta en corto automatica.");

        if (mode == TradeExecutionMode.Live && (!options.AllowLiveOrders || options.UseTestOrderEndpoint))
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "live-guard", "Modo Live bloqueado: requiere AllowLiveOrders=true y UseTestOrderEndpoint=false.");

        if (mode is TradeExecutionMode.Live or TradeExecutionMode.Test && !HasCredentials(options))
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "missing-credentials", $"Faltan {options.ApiKeyEnvironmentVariable}/{options.ApiSecretEnvironmentVariable}.");

        if (requestedCapital <= 0m)
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "capital-zero", "Capital automatico invalido.");

        var lastDayNet = await opportunityRepository.GetRealizedNetSinceAsync(DateTimeOffset.UtcNow.AddDays(-1), cancellationToken);
        if (options.DailyLossLimit > 0m && lastDayNet <= -Math.Abs(options.DailyLossLimit))
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "daily-loss-limit", $"Limite de perdida diaria alcanzado. Neto ultimas 24h: {lastDayNet:N2}.");

        return new ExecutionDecision(true, TradeExecutionStatus.Submitted, "allowed", "Senal aprobada por filtros automaticos.");
    }

    private static ExecutionDecision ValidateExit(OpportunityReportRow opportunity, TradeExecutionAudit? entry, ExchangeExecutionOptions options, TradeExecutionMode mode)
    {
        if (!options.Enabled)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "execution-disabled", "Ejecucion automatica desactivada al momento de salida.");

        if (!options.EnableExitOrders)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "exits-disabled", "Salidas automaticas desactivadas.");

        if (entry is null)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "no-entry", "No existe una entrada automatica previa para cerrar.");

        if (entry.Status is TradeExecutionStatus.Blocked or TradeExecutionStatus.Failed or TradeExecutionStatus.Skipped)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "entry-not-opened", "La entrada automatica previa no fue abierta, asi que no se envia salida.");

        if (mode == TradeExecutionMode.Live && (!options.AllowLiveOrders || options.UseTestOrderEndpoint))
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "live-guard", "Salida Live bloqueada: requiere AllowLiveOrders=true y UseTestOrderEndpoint=false.");

        if (mode is TradeExecutionMode.Live or TradeExecutionMode.Test && !HasCredentials(options))
            return new ExecutionDecision(false, TradeExecutionStatus.Blocked, "missing-credentials", $"Faltan {options.ApiKeyEnvironmentVariable}/{options.ApiSecretEnvironmentVariable}.");

        if (opportunity.Side == MarketSide.Short && !options.AllowShortSelling)
            return new ExecutionDecision(false, TradeExecutionStatus.Skipped, "short-disabled", "No hay venta en corto automatica habilitada para cerrar.");

        return new ExecutionDecision(true, TradeExecutionStatus.Submitted, "allowed", "Salida aprobada por filtros automaticos.");
    }

    private Task SaveAsync(OpportunityReportRow opportunity, TradeExecutionAction action, TradeExecutionMode mode, TradeExecutionStatus status, decimal requestedCapital, decimal? requestedQuantity,
        decimal? executedQuantity, decimal? executedQuote, decimal? price, string clientOrderId, string exchangeOrderId, string reason, string message, string requestJson, string responseJson,
        CancellationToken cancellationToken)
    {
        var audit = new TradeExecutionAudit(
            Guid.NewGuid(),
            opportunity.Id,
            opportunity.Symbol,
            opportunity.Side,
            action,
            mode,
            status,
            Math.Round(requestedCapital, 2),
            requestedQuantity,
            executedQuantity,
            executedQuote is null ? null : Math.Round(executedQuote.Value, 2),
            price,
            clientOrderId,
            exchangeOrderId,
            reason,
            message,
            requestJson,
            responseJson,
            DateTimeOffset.UtcNow);

        return executionRepository.SaveAsync(audit, cancellationToken);
    }

    private static bool HasCredentials(ExchangeExecutionOptions options)
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable))
               && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(options.ApiSecretEnvironmentVariable));
    }

    private static bool IsSymbolAllowed(string symbol, ExchangeExecutionOptions options)
    {
        var allowedSymbols = options.AllowedSymbols ?? [];

        return allowedSymbols.Length == 0 || allowedSymbols.Any(allowed => string.Equals(allowed, symbol, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPriceInsideEntryBand(OpportunityReportRow opportunity, decimal maxSlippagePercent)
    {
        var slippage = Math.Max(0m, maxSlippagePercent) / 100m;

        return opportunity.Side == MarketSide.Long
            ? opportunity.LastPrice <= opportunity.EntryUpper * (1m + slippage)
            : opportunity.LastPrice >= opportunity.EntryLower * (1m - slippage);
    }

    private static decimal ResolveCapital(OpportunityReportRow opportunity, ExchangeExecutionOptions options)
    {
        var maxCapital = Math.Max(0m, options.MaxCapitalPerTrade);

        if (maxCapital <= 0m)
            return 0m;

        return Math.Min(opportunity.Capital <= 0m ? maxCapital : opportunity.Capital, maxCapital);
    }

    private static decimal EstimateQuantity(decimal capital, decimal price)
    {
        return price <= 0m ? 0m : Math.Round(capital / price, 8);
    }

    private static decimal RoundDown(decimal value, decimal step)
    {
        if (value <= 0m || step <= 0m)
            return value;

        return Math.Floor(value / step) * step;
    }

    private static bool ShouldUseTestEndpoint(ExchangeExecutionOptions options, TradeExecutionMode mode)
    {
        return mode == TradeExecutionMode.Test || options.UseTestOrderEndpoint;
    }

    private static TradeExecutionMode ResolveMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "live" or "real" => TradeExecutionMode.Live,
            "test" or "binancetest" or "exchange-test" => TradeExecutionMode.Test,
            _ => TradeExecutionMode.Paper
        };
    }

    private static string ResolveBaseAsset(string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();

        if (normalized.EndsWith("USDT", StringComparison.Ordinal))
            return normalized[..^4];

        if (normalized.EndsWith("USD", StringComparison.Ordinal))
            return normalized[..^3];

        return normalized;
    }

    private static string BuildClientOrderId(string prefix, Guid opportunityId)
    {
        return $"{prefix}-{opportunityId:N}"[..Math.Min(36, $"{prefix}-{opportunityId:N}".Length)];
    }

    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private sealed record ExecutionDecision(bool Allowed, TradeExecutionStatus Status, string Reason, string Message);
}
