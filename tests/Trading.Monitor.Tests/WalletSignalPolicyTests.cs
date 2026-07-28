using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public sealed class WalletSignalPolicyTests
{
    [Fact]
    public void CanShowSignal_AllowsLongWithoutCoinBalance()
    {
        var wallet = new WalletSnapshot(1000m, false, []);

        Assert.True(WalletSignalPolicy.CanShowSignal(MarketSide.Long, "BTCUSDT", wallet));
    }

    [Fact]
    public void CanShowSignal_BlocksShortWhenCoinBalanceIsZero()
    {
        var wallet = new WalletSnapshot(
            1000m,
            false,
            [new WalletAssetPosition("BTCUSDT", "BTC", 0m, true, false, DateTimeOffset.UtcNow)]);

        Assert.False(WalletSignalPolicy.CanShowSignal(MarketSide.Short, "BTCUSDT", wallet));
    }

    [Fact]
    public void CanShowSignal_AllowsShortWhenCoinBalanceExistsAndRuleIsEnabled()
    {
        var wallet = new WalletSnapshot(
            1000m,
            false,
            [new WalletAssetPosition("BTCUSDT", "BTC", 0.15m, true, false, DateTimeOffset.UtcNow)]);

        Assert.True(WalletSignalPolicy.CanShowSignal(MarketSide.Short, "BTCUSDT", wallet));
    }
}
