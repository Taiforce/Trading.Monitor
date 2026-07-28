using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public static class WalletSignalPolicy
{
    public static bool CanShowSignal(OpportunityReportRow row, WalletSnapshot wallet)
    {
        return wallet.CanShowSignal(row.Side, row.Symbol);
    }

    public static bool CanShowSignal(MarketSide side, string symbol, WalletSnapshot wallet)
    {
        return wallet.CanShowSignal(side, symbol);
    }
}
