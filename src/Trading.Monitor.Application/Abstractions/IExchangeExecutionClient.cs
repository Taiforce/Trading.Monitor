using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Application.Abstractions;

public interface IExchangeExecutionClient
{
    Task<SymbolTradeRules> GetSymbolRulesAsync(string symbol, CancellationToken cancellationToken);

    Task<ExchangeBalance?> GetBalanceAsync(string asset, CancellationToken cancellationToken);

    Task<ExchangeOrderResult> PlaceMarketBuyAsync(string symbol, decimal quoteOrderQuantity, string clientOrderId, bool useTestEndpoint, CancellationToken cancellationToken);

    Task<ExchangeOrderResult> PlaceMarketSellAsync(string symbol, decimal quantity, string clientOrderId, bool useTestEndpoint, CancellationToken cancellationToken);
}
