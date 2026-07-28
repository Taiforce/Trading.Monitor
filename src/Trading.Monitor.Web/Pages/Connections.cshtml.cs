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
    ITradeExecutionRepository tradeExecutionRepository,
    IOptionsMonitor<ReportingOptions> reportingOptions,
    ExchangeConnectionStatusService exchangeConnectionStatusService,
    ILogger<ConnectionsModel> logger)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    public ExchangeConnectionStatus? ExchangeStatus { get; private set; }

    public IReadOnlyList<TradeExecutionAudit> RecentExecutions { get; private set; } = [];

    public IReadOnlyList<IGrouping<DataSourceKind, SourceHealthReportRow>> SourcesByKind { get; private set; } = [];

    public IReadOnlyList<SourceHealthReportRow> FilteredSources { get; private set; } = [];

    public IReadOnlyList<ConnectionCatalogItem> FilteredCatalog { get; private set; } = [];

    public IReadOnlyList<ConnectionConceptGroup> ConnectionGroups { get; private set; } = [];

    public IReadOnlyList<SourceHealthReportRow> ScopedSources { get; private set; } = [];

    public IReadOnlyList<ConnectionCatalogItem> ScopedCatalog { get; private set; } = [];

    public IReadOnlyList<DataSourceKind> AvailableKinds { get; private set; } = [];

    public IReadOnlyList<ConnectionCatalogItem> Catalog { get; } =
    [
        new("Mercado en vivo", "Binance Spot API", "En uso", "Velas 1s a 1M, precio y volumen para BTC, ETH, SOL, XRP y ADA.", "Sin llave para datos publicos.", "https://developers.binance.com/"),
        new("Mercado en vivo", "Binance US", "En uso", "Respaldo cuando Binance global falla.", "Sin llave para datos publicos.", "https://docs.binance.us/"),
        new("Mercado en vivo", "Coinbase Exchange", "En uso", "Precio y velas spot como fuente alternativa.", "Sin llave para datos publicos.", "https://docs.cdp.coinbase.com/exchange/"),
        new("Mercado en vivo", "Kraken", "En uso", "OHLC spot para validar precios con otro exchange.", "Sin llave para datos publicos.", "https://docs.kraken.com/api/"),
        new("Mercado forex", "Yahoo Finance FX", "En uso", "Velas para pares Forex principales como EUR/USD, GBP/USD, USD/JPY y USD/MXN.", "Sin llave; fuente publica con limites no garantizados.", "https://finance.yahoo.com/currencies"),
        new("Mercado forex", "Alpha Vantage FX", "Opcional", "Velas intradia, diaria, semanal y mensual para pares de divisas.", "Requiere ALPHA_VANTAGE_API_KEY para uso continuo.", "https://www.alphavantage.co/documentation/"),
        new("Broker forex", "OANDA v20 API", "Candidato", "Precios, cuentas, ordenes y trading programatico Forex.", "Requiere cuenta OANDA, token y configuracion de riesgo.", "https://developer.oanda.com/rest-live-v20/introduction/"),
        new("Noticias", "RSS crypto y mercados", "En uso", "CoinDesk, Cointelegraph, Decrypt, CryptoSlate, Yahoo Finance, CNBC, MarketWatch e Investing.", "Sin llave.", "https://feeds.finance.yahoo.com/rss/2.0/headline?s=BTC-USD,ETH-USD,SOL-USD,XRP-USD,ADA-USD&region=US&lang=en-US"),
        new("Noticias forex", "Myfxbook RSS", "En uso", "Noticias Forex y calendario economico para eventos que mueven divisas.", "Sin llave para RSS individual/no comercial.", "https://www.myfxbook.com/rss"),
        new("Macro forex", "Bancos centrales", "En uso", "Fed, ECB, BoJ, BoE y Banxico para tasas, discursos y comunicados.", "RSS o paginas oficiales sin llave.", "https://www.federalreserve.gov/feeds/feeds.htm"),
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
        new("On-chain", "Glassnode/Santiment", "Candidato", "Flujos on-chain, exchanges, holders, realizacion y actividad de red.", "Requiere plan.", "https://docs.glassnode.com/"),
        new("Traders", "eToro Popular Investor", "Candidato", "Ranking publico de copy trading para estudiar consistencia, drawdown y activos.", "Requiere revisar terminos, costos y disponibilidad por pais.", "https://www.etoro.com/copytrader/"),
        new("Traders", "ZuluTrade", "Candidato", "Copy trading multi-activo con historiales publicos de proveedores.", "Requiere cuenta compatible y validacion de riesgo.", "https://www.zulutrade.com/"),
        new("Traders", "Axi Copy Trading", "Candidato", "Perfiles de traders forex para copiar o estudiar manualmente.", "Requiere cuenta Axi y validacion regulatoria.", "https://www.axi.com/int/copy-trading"),
        new("Traders", "TradingView Ideas", "En uso", "Ideas publicas de traders para crypto y forex, utiles como investigacion externa.", "Sin llave para lectura manual; scraping/API depende de permisos.", "https://www.tradingview.com/ideas/"),
        new("Traders", "Myfxbook Systems", "Candidato", "Sistemas forex con metricas historicas y drawdown verificable cuando el perfil lo permite.", "Requiere acceso de fuente y reglas de uso.", "https://www.myfxbook.com/")
    ];

    public int CatalogInUseCount => Catalog.Count(item => item.Status == "En uso");

    public int CatalogCandidateCount => Catalog.Count(item => item.Status != "En uso");

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "todas";

    [BindProperty(SupportsGet = true)]
    public string Tipo { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Buscar { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Ambito { get; set; } = "todo";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading connections page.");
        await LoadReportAsync(cancellationToken);
        ExchangeStatus = await exchangeConnectionStatusService.GetAsync(cancellationToken);
        RecentExecutions = await tradeExecutionRepository.GetRecentAsync(40, cancellationToken);

        Ambito = NormalizeScope(Ambito);
        ScopedSources = Report.SourceHealth.Where(MatchesScope).ToArray();
        ScopedCatalog = Catalog.Where(MatchesScope).ToArray();
        AvailableKinds = ScopedSources.Select(row => row.Kind).Distinct().OrderBy(row => row).ToArray();
        FilteredSources = ApplyFilters(ScopedSources);
        FilteredCatalog = ApplyCatalogFilters(ScopedCatalog);
        SourcesByKind = FilteredSources.GroupBy(row => row.Kind).OrderBy(group => group.Key).ToArray();
        ConnectionGroups = BuildConnectionGroups();
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

        return sources.OrderBy(source => ConceptFor(source.Kind, source.SourceName))
            .ThenBy(source => source.Status)
            .ThenBy(source => source.SourceName)
            .ToArray();
    }

    private IReadOnlyList<ConnectionCatalogItem> ApplyCatalogFilters(IEnumerable<ConnectionCatalogItem> catalog)
    {
        if (!string.IsNullOrWhiteSpace(Buscar))
        {
            catalog = catalog.Where(item =>
                item.Name.Contains(Buscar, StringComparison.OrdinalIgnoreCase) ||
                item.Group.Contains(Buscar, StringComparison.OrdinalIgnoreCase) ||
                item.Use.Contains(Buscar, StringComparison.OrdinalIgnoreCase) ||
                item.Requirement.Contains(Buscar, StringComparison.OrdinalIgnoreCase) ||
                item.Url.Contains(Buscar, StringComparison.OrdinalIgnoreCase));
        }

        catalog = Estado?.Trim().ToLowerInvariant() switch
        {
            "sanas" => catalog.Where(item => item.Status == "En uso"),
            "degradadas" => catalog.Where(item => item.Status is "Opcional" or "Candidato"),
            "fallidas" => [],
            _ => catalog
        };

        if (Enum.TryParse<DataSourceKind>(Tipo, true, out var kind))
        {
            var concept = ConceptFor(kind, "");
            catalog = catalog.Where(item => ConceptFor(item.Group, item.Name) == concept);
        }

        return catalog.OrderBy(item => ConceptFor(item.Group, item.Name)).ThenBy(item => item.Name).ToArray();
    }

    private IReadOnlyList<ConnectionConceptGroup> BuildConnectionGroups()
    {
        var concepts = new[] { "Market", "Noticias", "IA", "Traders" };
        return concepts
            .Select(concept => new ConnectionConceptGroup(
                concept,
                ScopedSources.Where(source => ConceptFor(source.Kind, source.SourceName) == concept).ToArray(),
                ScopedCatalog.Where(item => ConceptFor(item.Group, item.Name) == concept).ToArray(),
                FilteredSources.Where(source => ConceptFor(source.Kind, source.SourceName) == concept).ToArray(),
                FilteredCatalog.Where(item => ConceptFor(item.Group, item.Name) == concept).ToArray()))
            .Where(group => group.Total > 0)
            .ToArray();
    }

    private bool MatchesScope(SourceHealthReportRow source)
    {
        return MatchesScope($"{source.SourceName} {source.Kind} {source.LastMessage} {source.Url}");
    }

    private bool MatchesScope(ConnectionCatalogItem item)
    {
        return MatchesScope($"{item.Group} {item.Name} {item.Use} {item.Requirement} {item.Url}");
    }

    private bool MatchesScope(string text)
    {
        return Ambito switch
        {
            "crypto" => ContainsAny(text, "crypto", "cripto", "BTC", "ETH", "SOL", "XRP", "ADA", "USDT", "Binance", "Coinbase", "Kraken", "CoinGecko", "CoinGlass", "LunarCrush", "Glassnode", "Santiment", "CryptoPanic", "Fear"),
            "forex" => ContainsAny(text, "forex", "FX", "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD", "USDMXN", "OANDA", "Yahoo Finance FX", "Alpha Vantage FX", "Myfxbook", "Bancos centrales", "Fed", "ECB", "Banxico"),
            "traders" => ContainsAny(text, "trader", "copy", "eToro", "ZuluTrade", "Axi", "TradingView", "Myfxbook", "historial", "perfil"),
            _ => true
        };
    }

    public string ScopeLabel()
    {
        return Ambito switch
        {
            "crypto" => "Crypto",
            "forex" => "Forex",
            "traders" => "Traders",
            _ => "Todo"
        };
    }

    public string ConceptDescription(string concept)
    {
        return concept switch
        {
            "Market" => "Precios, velas, brokers y datos directos de mercado.",
            "Noticias" => "Noticias, macro, sentimiento y eventos que pueden mover el precio.",
            "IA" => "Modelos y análisis automatizado que resumen o califican oportunidades.",
            "Traders" => "Fuentes para estudiar traders, copy trading e historiales públicos.",
            _ => "Fuentes operativas del sistema."
        };
    }

    public string ConceptSearchSummary(string concept)
    {
        return concept switch
        {
            "Market" => "Busca precio vivo, velas, volumen, continuidad, latencia y diferencias entre proveedores.",
            "Noticias" => "Busca titulares, eventos macro, sentimiento, regulaciÃ³n y catalizadores que puedan mover el mercado.",
            "IA" => "Resume noticias, compara argumentos, detecta vetos y convierte ruido externo en una lectura accionable.",
            "Traders" => "Busca fuentes de copy trading, perfiles, historiales verificables, trades abiertos y resultados cerrados.",
            _ => "Busca informaciÃ³n operativa que pueda alimentar el anÃ¡lisis del sistema."
        };
    }

    public string SourceQueryDetail(SourceHealthReportRow source)
    {
        if (source.Status == DataSourceStatus.Failed)
            return $"Ultima consulta fallida. {source.LastMessage} Fallos acumulados: {source.FailureCount}.";

        if (source.Status == DataSourceStatus.Degraded)
            return $"Consulta parcial o degradada. {source.LastMessage}";

        return $"Ultima consulta util: {source.LastMessage}";
    }

    public string CatalogQueryDetail(ConnectionCatalogItem item)
    {
        return $"{item.Use} Requisito operativo: {item.Requirement}.";
    }

    public string ConceptClass(string concept)
    {
        return concept switch
        {
            "Market" => "status-open",
            "Noticias" => "status-muted",
            "IA" => "status-win",
            "Traders" => "status-open",
            _ => "status-muted"
        };
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

    public string ExecutionStatusClass(TradeExecutionStatus status)
    {
        return status switch
        {
            TradeExecutionStatus.Simulated or TradeExecutionStatus.Filled => "status-win",
            TradeExecutionStatus.Submitted => "status-open",
            TradeExecutionStatus.Blocked or TradeExecutionStatus.Failed => "status-loss",
            _ => "status-muted"
        };
    }

    public string ExecutionActionLabel(TradeExecutionAction action)
    {
        return action switch
        {
            TradeExecutionAction.BuyToOpen => "Comprar",
            TradeExecutionAction.SellToClose => "Vender",
            TradeExecutionAction.SellToOpen => "Vender primero",
            TradeExecutionAction.BuyToClose => "Comprar de regreso",
            _ => action.ToString()
        };
    }

    public string ExecutionModeLabel(TradeExecutionMode mode)
    {
        return mode switch
        {
            TradeExecutionMode.Paper => "Simulado",
            TradeExecutionMode.Test => "Test Binance",
            TradeExecutionMode.Live => "Real",
            _ => mode.ToString()
        };
    }

    private static string NormalizeScope(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "crypto" => "crypto",
            "forex" => "forex",
            "traders" => "traders",
            _ => "todo"
        };
    }

    private static string ConceptFor(DataSourceKind kind, string sourceName)
    {
        return kind switch
        {
            DataSourceKind.MarketData => "Market",
            DataSourceKind.AiAnalysis => "IA",
            DataSourceKind.Research when ContainsAny(sourceName, "trader", "copy", "TradingView", "Myfxbook", "eToro", "Axi", "Zulu") => "Traders",
            DataSourceKind.News or DataSourceKind.MacroReport or DataSourceKind.SocialSentiment or DataSourceKind.Research => "Noticias",
            _ => "Market"
        };
    }

    private static string ConceptFor(string group, string name)
    {
        var text = $"{group} {name}";
        if (ContainsAny(text, "IA", "AI", "OpenAI", "Kensho", "Tickeron", "TrendSpider"))
            return "IA";

        if (ContainsAny(text, "Trader", "Copy", "eToro", "Zulu", "Axi", "TradingView", "Myfxbook"))
            return "Traders";

        if (ContainsAny(text, "Noticias", "News", "Macro", "Sentimiento", "Eventos", "Bancos", "RSS", "FRED", "Fear"))
            return "Noticias";

        return "Market";
    }

    private static bool ContainsAny(string value, params string[] patterns)
    {
        return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record ConnectionCatalogItem(string Group, string Name, string Status, string Use, string Requirement, string Url);

public sealed record ConnectionConceptGroup(
    string Name,
    IReadOnlyList<SourceHealthReportRow> AllSources,
    IReadOnlyList<ConnectionCatalogItem> AllCatalog,
    IReadOnlyList<SourceHealthReportRow> Sources,
    IReadOnlyList<ConnectionCatalogItem> Catalog)
{
    public int Total => AllSources.Count + AllCatalog.Count;

    public int Visible => Sources.Count + Catalog.Count;

    public int Healthy => AllSources.Count(source => source.Status == DataSourceStatus.Healthy) + AllCatalog.Count(item => item.Status == "En uso");

    public int Failed => AllSources.Count(source => source.Status == DataSourceStatus.Failed);
}
