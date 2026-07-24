using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;
using Trading.Monitor.Web.Services;

namespace Trading.Monitor.Web.Pages;

public sealed class ConnectionsModel(
    IOpportunityRepository opportunityRepository,
    IOptionsMonitor<ReportingOptions> reportingOptions,
    ExchangeConnectionStatusService exchangeConnectionStatusService,
    ILogger<ConnectionsModel> logger)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    public ExchangeConnectionStatus? ExchangeStatus { get; private set; }

    public IReadOnlyList<IGrouping<DataSourceKind, SourceHealthReportRow>> SourcesByKind { get; private set; } = [];

    public IReadOnlyList<SourceHealthReportRow> FilteredSources { get; private set; } = [];

    public IReadOnlyList<DataSourceKind> AvailableKinds { get; private set; } = [];

    public IReadOnlyList<ConnectionCatalogItem> Catalog { get; } =
    [
        new("Mercado en vivo", "Binance Spot API", "En uso", "Velas 1s a 1M, precio y volumen para BTC/ETH.", "Sin llave para datos publicos.", "https://developers.binance.com/"),
        new("Mercado en vivo", "Binance US", "En uso", "Respaldo cuando Binance global falla.", "Sin llave para datos publicos.", "https://docs.binance.us/"),
        new("Mercado en vivo", "Coinbase Exchange", "En uso", "Precio y velas spot como fuente alternativa.", "Sin llave para datos publicos.", "https://docs.cdp.coinbase.com/exchange/"),
        new("Mercado en vivo", "Kraken", "En uso", "OHLC spot para validar precios con otro exchange.", "Sin llave para datos publicos.", "https://docs.kraken.com/api/"),
        new("Noticias", "RSS crypto y mercados", "En uso", "CoinDesk, Cointelegraph, Decrypt, CryptoSlate, Yahoo Finance, CNBC, MarketWatch e Investing.", "Sin llave.", "https://feeds.finance.yahoo.com/rss/2.0/headline?s=BTC-USD,ETH-USD&region=US&lang=en-US"),
        new("Sentimiento", "Fear & Greed", "En uso", "Mide apetito/riesgo general del mercado cripto.", "Sin llave.", "https://alternative.me/crypto/fear-and-greed-index/"),
        new("Noticias", "CryptoPanic", "Opcional", "Noticias estructuradas por moneda.", "Requiere CRYPTOPANIC_AUTH_TOKEN.", "https://cryptopanic.com/developers/api/"),
        new("IA", "OpenAI", "En uso", "Resume noticias y reduce ruido informativo.", "Requiere OPENAI_API_KEY.", "https://platform.openai.com/docs"),
        new("Mercado cripto", "CoinGecko", "Candidato", "Market cap, volumen, precios, categorias, exchanges y datos on-chain agregados.", "Requiere plan/llave para uso intensivo.", "https://docs.coingecko.com/"),
        new("Eventos", "CoinMarketCal", "Candidato", "Calendario de catalizadores: forks, desbloqueos, listados, upgrades.", "Requiere llave API.", "https://coinmarketcal.com/developer/docs"),
        new("DeFi/on-chain", "DefiLlama", "Candidato", "TVL, stablecoins, yields, DEX volumen, fees y revenue.", "Muchas rutas publicas; Pro opcional.", "https://api-docs.defillama.com/"),
        new("Macro", "FRED", "Candidato", "CPI, tasas, liquidez, empleo y series economicas que mueven riesgo.", "Requiere llave FRED.", "https://fred.stlouisfed.org/docs/api/fred/"),
        new("Acciones", "Polygon.io", "Candidato", "Trades, quotes, aggregates y noticias de bolsa.", "Requiere llave/plan.", "https://polygon.io/docs"),
        new("Acciones", "Alpaca Market Data", "Candidato", "Datos y noticias para acciones USA y paper trading.", "Requiere llave.", "https://docs.alpaca.markets/"),
        new("Sentimiento", "LunarCrush", "Candidato", "Social trend, engagement y sentimiento crypto.", "Requiere llave/plan.", "https://lunarcrush.com/developers"),
        new("Derivados", "CoinGlass", "Candidato", "Liquidaciones, funding, open interest y long/short ratios.", "Requiere plan/API.", "https://www.coinglass.com/api"),
        new("On-chain", "Glassnode/Santiment", "Candidato", "Flujos on-chain, exchanges, holders, realizacion y actividad de red.", "Requiere plan.", "https://docs.glassnode.com/")
    ];

    public int CatalogInUseCount => Catalog.Count(item => item.Status == "En uso");

    public int CatalogCandidateCount => Catalog.Count(item => item.Status != "En uso");

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "todas";

    [BindProperty(SupportsGet = true)]
    public string Tipo { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Buscar { get; set; } = "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading connections page.");
        await LoadReportAsync(cancellationToken);
        ExchangeStatus = await exchangeConnectionStatusService.GetAsync(cancellationToken);

        AvailableKinds = Report.SourceHealth.Select(row => row.Kind).Distinct().OrderBy(row => row).ToArray();
        FilteredSources = ApplyFilters(Report.SourceHealth);
        SourcesByKind = FilteredSources.GroupBy(row => row.Kind).OrderBy(group => group.Key).ToArray();
    }

    private IReadOnlyList<SourceHealthReportRow> ApplyFilters(IEnumerable<SourceHealthReportRow> sources)
    {
        if (!string.IsNullOrWhiteSpace(Buscar))
        {
            sources = sources.Where(source =>
                source.SourceName.Contains(Buscar, StringComparison.OrdinalIgnoreCase) ||
                (source.Url?.Contains(Buscar, StringComparison.OrdinalIgnoreCase) ?? false) ||
                source.LastMessage.Contains(Buscar, StringComparison.OrdinalIgnoreCase));
        }

        if (Enum.TryParse<DataSourceKind>(Tipo, true, out var kind))
            sources = sources.Where(source => source.Kind == kind);

        sources = Estado?.Trim().ToLowerInvariant() switch
        {
            "sanas" => sources.Where(source => source.Status == DataSourceStatus.Healthy),
            "degradadas" => sources.Where(source => source.Status == DataSourceStatus.Degraded),
            "fallidas" => sources.Where(source => source.Status == DataSourceStatus.Failed),
            _ => sources
        };

        return sources.OrderBy(source => source.Kind).ThenBy(source => source.Status).ThenBy(source => source.SourceName).ToArray();
    }

    public string CatalogStatusClass(string status)
    {
        return status switch
        {
            "En uso" => "status-win",
            "Opcional" => "status-muted",
            _ => "status-open"
        };
    }
}

public sealed record ConnectionCatalogItem(string Group, string Name, string Status, string Use, string Requirement, string Url);
