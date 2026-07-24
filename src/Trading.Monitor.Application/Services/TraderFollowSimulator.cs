using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Application.Services;

public sealed class TraderFollowSimulator
{
    public TraderFollowSimulationReport Simulate(IEnumerable<TraderTradeReportRow> trades, decimal initialCapital, decimal feePercentPerSide)
    {
        var balance = initialCapital <= 0m ? 0m : initialCapital;
        var peak = balance;
        var maxDrawdown = 0m;
        var sequence = 0;
        var rows = new List<TraderFollowTradeRow>();
        var equity = new List<TraderFollowEquityPoint> { new(0, DateTimeOffset.UtcNow, balance) };

        foreach (var trade in trades.OrderBy(row => row.OpenedAt))
        {
            sequence++;
            var startBalance = balance;
            var wasApplied = string.Equals(trade.Status, "Cerrada", StringComparison.OrdinalIgnoreCase)
                && trade.ExitPrice.HasValue
                && trade.EntryPrice > 0m
                && balance > 0m;
            var quantity = 0m;
            var fees = 0m;
            var net = 0m;
            var endBalance = balance;
            var skipReason = "";

            if (wasApplied)
            {
                quantity = Math.Round(balance / trade.EntryPrice, 8);
                fees = Math.Round(balance * (feePercentPerSide / 100m) * 2m, 2);
                var gross = OpportunityProjectionService.CalculateGrossPnL(trade.Side, trade.EntryPrice, trade.ExitPrice!.Value, quantity);
                net = Math.Round(gross - fees, 2);
                endBalance = Math.Round(balance + net, 2);
                balance = endBalance;
                peak = Math.Max(peak, balance);

                if (peak > 0m)
                    maxDrawdown = Math.Max(maxDrawdown, Math.Round((peak - balance) / peak * 100m, 2));
            }
            else
            {
                skipReason = string.Equals(trade.Status, "Abierta", StringComparison.OrdinalIgnoreCase) ? "Abierta: no se calcula cierre" : "Sin precio de salida";
            }

            rows.Add(new TraderFollowTradeRow(
                sequence,
                trade.TraderName,
                trade.Platform,
                trade.Symbol,
                trade.SignalType,
                trade.OpenedAt,
                trade.ClosedAt,
                trade.EntryPrice,
                trade.ExitPrice,
                startBalance,
                quantity,
                fees,
                net,
                endBalance,
                startBalance <= 0m ? 0m : Math.Round(net / startBalance * 100m, 2),
                wasApplied,
                skipReason));

            equity.Add(new TraderFollowEquityPoint(sequence, trade.ClosedAt ?? trade.OpenedAt, balance));
        }

        var applied = rows.Where(row => row.WasApplied).ToArray();
        var netPnL = Math.Round(balance - initialCapital, 2);

        return new TraderFollowSimulationReport(
            initialCapital,
            balance,
            netPnL,
            initialCapital <= 0m ? 0m : Math.Round(netPnL / initialCapital * 100m, 2),
            peak,
            maxDrawdown,
            rows.Count,
            applied.Length,
            rows.Count - applied.Length,
            applied.Count(row => row.NetPnL > 0m),
            applied.Count(row => row.NetPnL < 0m),
            rows,
            equity);
    }
}
