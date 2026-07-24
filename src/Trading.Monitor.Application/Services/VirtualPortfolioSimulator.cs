using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed class VirtualPortfolioSimulator
{
    public VirtualPortfolioReport Simulate(IEnumerable<OpportunityReportRow> signals, decimal initialCapital, decimal feePercentPerSide)
    {
        initialCapital = initialCapital <= 0m ? 1m : Math.Round(initialCapital, 2);
        var balance = initialCapital;
        var peak = initialCapital;
        var maxDrawdown = 0m;
        var sequence = 0;
        var applied = 0;
        var skipped = 0;
        var winners = 0;
        var losers = 0;
        var trades = new List<VirtualPortfolioTradeRow>();
        var equityPoints = new List<VirtualPortfolioEquityPoint> { new(0, DateTimeOffset.UtcNow, initialCapital) };

        foreach (var signal in signals.OrderBy(signal => signal.ObservedAt))
        {
            sequence++;
            var startBalance = balance;
            var canApply = signal.Status != OpportunityStatus.Open && signal.ExitPrice.HasValue && startBalance > 0m;
            var skipReason = canApply ? "" : ResolveSkipReason(signal, startBalance);
            var quantity = 0m;
            var fees = 0m;
            var netPnL = 0m;
            var returnPercent = 0m;

            if (canApply)
            {
                quantity = signal.EntryPrice <= 0m ? 0m : startBalance / signal.EntryPrice;
                fees = startBalance * (feePercentPerSide / 100m) * 2m;
                var gross = OpportunityProjectionService.CalculateGrossPnL(signal.Side, signal.EntryPrice, signal.ExitPrice!.Value, quantity);
                netPnL = Math.Round(gross - fees, 2);
                balance = Math.Round(startBalance + netPnL, 2);
                returnPercent = startBalance <= 0m ? 0m : Math.Round(netPnL / startBalance * 100m, 2);
                applied++;

                if (netPnL > 0m)
                    winners++;
                else if (netPnL < 0m)
                    losers++;

                if (balance > peak)
                    peak = balance;

                if (peak > 0m)
                    maxDrawdown = Math.Max(maxDrawdown, Math.Round((peak - balance) / peak * 100m, 2));

                equityPoints.Add(new VirtualPortfolioEquityPoint(sequence, signal.ExitTime ?? signal.ExpiresAt, balance));
            }
            else
            {
                skipped++;
            }

            trades.Add(new VirtualPortfolioTradeRow(
                sequence,
                signal.Id,
                signal.Symbol,
                HorizonFor(signal),
                SignalTypeDescriptor.Label(signal.Side),
                signal.ObservedAt,
                signal.ObservedAt,
                signal.ExitTime,
                signal.EntryPrice,
                signal.ExitPrice,
                Math.Round(startBalance, 2),
                Math.Round(quantity, 8),
                Math.Round(fees, 2),
                netPnL,
                Math.Round(balance, 2),
                returnPercent,
                canApply,
                skipReason));
        }

        var net = Math.Round(balance - initialCapital, 2);
        var returnTotal = initialCapital <= 0m ? 0m : Math.Round(net / initialCapital * 100m, 2);

        return new VirtualPortfolioReport(
            initialCapital,
            Math.Round(balance, 2),
            net,
            returnTotal,
            Math.Round(peak, 2),
            maxDrawdown,
            sequence,
            applied,
            skipped,
            winners,
            losers,
            trades,
            equityPoints);
    }

    private static string ResolveSkipReason(OpportunityReportRow signal, decimal balance)
    {
        if (balance <= 0m)
            return "Sin saldo";

        if (signal.Status == OpportunityStatus.Open)
            return "Abierta";

        if (!signal.ExitPrice.HasValue)
            return "Sin cierre";

        return "No aplicada";
    }

    private static string HorizonFor(OpportunityReportRow row)
    {
        var minutes = Math.Max(1, (row.ExpiresAt - row.ObservedAt).TotalMinutes);

        return minutes switch
        {
            <= 30 => "Rapida",
            <= 240 => "Intradia",
            <= 2880 => "Swing",
            <= 10080 => "Semanal",
            _ => "Mensual"
        };
    }
}
