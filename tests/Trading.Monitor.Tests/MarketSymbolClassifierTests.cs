using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public sealed class MarketSymbolClassifierTests
{
    [Theory]
    [InlineData("BTCUSDT")]
    [InlineData("ethusdt")]
    [InlineData("SOL-USDT")]
    public void GetMarketKind_RecognizesCryptoSymbols(string symbol)
    {
        Assert.Equal(MarketKind.Crypto, MarketSymbolClassifier.GetMarketKind(symbol));
    }

    [Theory]
    [InlineData("EURUSD")]
    [InlineData("GBP/USD")]
    [InlineData("USD-MXN")]
    public void GetMarketKind_RecognizesForexPairs(string symbol)
    {
        Assert.Equal(MarketKind.Forex, MarketSymbolClassifier.GetMarketKind(symbol));
    }

    [Fact]
    public void BuildSymbolList_UsesForexDefaultsWhenRequested()
    {
        var symbols = MarketSymbolClassifier.BuildSymbolList(["BTCUSDT", "EURUSD", "USDMXN"], "forex");

        Assert.Contains("EURUSD", symbols);
        Assert.Contains("USDMXN", symbols);
        Assert.DoesNotContain("BTCUSDT", symbols);
    }
}
