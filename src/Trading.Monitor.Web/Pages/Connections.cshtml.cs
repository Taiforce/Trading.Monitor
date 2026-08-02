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

    public IReadOnlyList<SourceHealthReportRow> FilteredSources { get; private set; } = [];

    public IReadOnlyList<ConnectionCatalogItem> FilteredCatalog { get; private set; } = [];

    public IReadOnlyList<ConnectionConceptGroup> ConnectionGroups { get; private set; } = [];

    public IReadOnlyList<SourceHealthReportRow> ScopedSources { get; private set; } = [];

    public IReadOnlyList<ConnectionCatalogItem> ScopedCatalog { get; private set; } = [];

    public IReadOnlyList<ConnectionCatalogItem> Catalog { get; } =
    [
        new("Mercado en vivo", "Binance Spot API", "En uso", "Velas 1s a 1M, precio y volumen para BTC, ETH, SOL, XRP y ADA.", "Sin llave para datos públicos.", "https://developers.binance.com/"),
        new("Mercado en vivo", "Binance US", "En uso", "Respaldo cuando Binance global falla.", "Sin llave para datos públicos.", "https://docs.binance.us/"),
        new("Mercado en vivo", "Coinbase Exchange", "En uso", "Precio y velas spot como fuente alternativa.", "Sin llave para datos públicos.", "https://docs.cdp.coinbase.com/exchange/"),
        new("Mercado en vivo", "Kraken", "En uso", "OHLC spot para validar precios con otro exchange.", "Sin llave para datos públicos.", "https://docs.kraken.com/api/"),
        new("Mercado forex", "Yahoo Finance FX", "En uso", "Velas para pares Forex principales como EUR/USD, GBP/USD, USD/JPY y USD/MXN.", "Sin llave; fuente pública con límites no garantizados.", "https://finance.yahoo.com/currencies"),
        new("Mercado forex", "Alpha Vantage FX", "Opcional", "Velas intradía, diaria, semanal y mensual para pares de divisas.", "Requiere ALPHA_VANTAGE_API_KEY para uso continuo.", "https://www.alphavantage.co/documentation/"),
        new("Broker forex", "OANDA v20 API", "Candidato", "Precios, cuentas, órdenes y trading programático Forex.", "Requiere cuenta OANDA, token y configuración de riesgo.", "https://developer.oanda.com/rest-live-v20/introduction/"),
        new("Noticias", "RSS crypto y mercados", "En uso", "CoinDesk, Cointelegraph, Decrypt, CryptoSlate, Yahoo Finance, CNBC, MarketWatch e Investing.", "Sin llave.", "https://feeds.finance.yahoo.com/rss/2.0/headline?s=BTC-USD,ETH-USD,SOL-USD,XRP-USD,ADA-USD&region=US&lang=en-US"),
        new("Noticias forex", "Myfxbook RSS", "En uso", "Noticias Forex y calendario económico para eventos que mueven divisas.", "Sin llave para RSS individual/no comercial.", "https://www.myfxbook.com/rss"),
        new("Macro forex", "Bancos centrales", "En uso", "Fed, ECB, BoJ, BoE y Banxico para tasas, discursos y comunicados.", "RSS o páginas oficiales sin llave.", "https://www.federalreserve.gov/feeds/feeds.htm"),
        new("Sentimiento", "Fear & Greed", "En uso", "Mide apetito/riesgo general del mercado cripto.", "Sin llave.", "https://alternative.me/crypto/fear-and-greed-index/"),
        new("Noticias", "CryptoPanic", "Opcional", "Noticias estructuradas por moneda.", "Requiere CRYPTOPANIC_AUTH_TOKEN.", "https://cryptopanic.com/developers/api/"),
        new("IA", "OpenAI", "En uso", "Resume noticias y reduce ruido informativo.", "Requiere OPENAI_API_KEY.", "https://platform.openai.com/docs"),
        new("Mercado cripto", "CoinGecko", "Candidato", "Market cap, volumen, precios, categorías, exchanges y datos on-chain agregados.", "Requiere plan/llave para uso intensivo.", "https://docs.coingecko.com/"),
        new("Eventos", "CoinMarketCal", "Candidato", "Calendario de catalizadores: forks, desbloqueos, listados, upgrades.", "Requiere llave API.", "https://coinmarketcal.com/developer/docs"),
        new("DeFi/on-chain", "DefiLlama", "Candidato", "TVL, stablecoins, yields, DEX volumen, fees y revenue.", "Muchas rutas públicas; Pro opcional.", "https://api-docs.defillama.com/"),
        new("Macro", "FRED", "Candidato", "CPI, tasas, liquidez, empleo y series económicas que mueven riesgo.", "Requiere llave FRED.", "https://fred.stlouisfed.org/docs/api/fred/"),
        new("Acciones", "Polygon.io", "Candidato", "Trades, quotes, aggregates y noticias de bolsa.", "Requiere llave/plan.", "https://polygon.io/docs"),
        new("Acciones", "Alpaca Market Data", "Candidato", "Datos y noticias para acciones USA y paper trading.", "Requiere llave.", "https://docs.alpaca.markets/"),
        new("Sentimiento", "LunarCrush", "Candidato", "Social trend, engagement y sentimiento crypto.", "Requiere llave/plan.", "https://lunarcrush.com/developers"),
        new("Derivados", "CoinGlass", "Candidato", "Liquidaciones, funding, open interest y long/short ratios.", "Requiere plan/API.", "https://www.coinglass.com/api"),
        new("On-chain", "Glassnode/Santiment", "Candidato", "Flujos on-chain, exchanges, holders, realización y actividad de red.", "Requiere plan.", "https://docs.glassnode.com/"),
        new("Traders", "eToro Popular Investor", "Candidato", "Ranking público de copy trading para estudiar consistencia, drawdown y activos.", "Requiere revisar términos, costos y disponibilidad por país.", "https://www.etoro.com/copytrader/"),
        new("Traders", "ZuluTrade", "Candidato", "Copy trading multi-activo con historiales públicos de proveedores.", "Requiere cuenta compatible y validación de riesgo.", "https://www.zulutrade.com/"),
        new("Traders", "Axi Copy Trading", "Candidato", "Perfiles de traders forex para copiar o estudiar manualmente.", "Requiere cuenta Axi y validación regulatoria.", "https://www.axi.com/int/copy-trading"),
        new("Traders", "TradingView Ideas", "En uso", "Ideas públicas de traders para crypto y forex, útiles como investigación externa.", "Sin llave para lectura manual; scraping/API depende de permisos.", "https://www.tradingview.com/ideas/"),
        new("Traders", "Myfxbook Systems", "Candidato", "Sistemas forex con métricas históricas y drawdown verificable cuando el perfil lo permite.", "Requiere acceso de fuente y reglas de uso.", "https://www.myfxbook.com/")
    ];

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
        FilteredSources = ScopedSources;
        FilteredCatalog = ScopedCatalog;
        ConnectionGroups = BuildConnectionGroups();
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
            "Noticias" => "Busca titulares, eventos macro, sentimiento, regulación y catalizadores que puedan mover el mercado.",
            "IA" => "Resume noticias, compara argumentos, detecta vetos y convierte ruido externo en una lectura accionable.",
            "Traders" => "Busca fuentes de copy trading, perfiles, historiales verificables, trades abiertos y resultados cerrados.",
            _ => "Busca información operativa que pueda alimentar el análisis del sistema."
        };
    }

    public string SourceQueryDetail(SourceHealthReportRow source)
    {
        if (source.Status == DataSourceStatus.Failed)
            return $"Última consulta fallida. {source.LastMessage} Fallos acumulados: {source.FailureCount}.";

        if (source.Status == DataSourceStatus.Degraded)
            return $"Consulta parcial o degradada. {source.LastMessage}";

        return $"Última consulta útil: {source.LastMessage}";
    }

    public string CatalogQueryDetail(ConnectionCatalogItem item)
    {
        return $"{item.Use} Requisito operativo: {item.Requirement}.";
    }

    public ConnectionInsight SourceInsight(SourceHealthReportRow source, string concept)
    {
        var extracted = source.Kind switch
        {
            DataSourceKind.MarketData => "Velas, precio actual, continuidad del proveedor, volumen y diferencia contra otras fuentes de mercado.",
            DataSourceKind.News => "Titulares, antigüedad de la noticia, símbolos mencionados y tono general que puede mover el precio.",
            DataSourceKind.MacroReport => "Eventos macro, bancos centrales, tasas, empleo, inflación y condiciones que afectan riesgo.",
            DataSourceKind.SocialSentiment => "Lectura de apetito o miedo del mercado para no entrar contra una reacción emocional fuerte.",
            DataSourceKind.AiAnalysis => "Resumen de noticias, argumentos a favor/en contra y reducción de ruido antes de puntuar una oportunidad.",
            _ => "Información externa para validar contexto, traders, reportes o investigación complementaria."
        };
        var obtained = source.Status switch
        {
            DataSourceStatus.Healthy => $"Última respuesta útil: {source.LastMessage}",
            DataSourceStatus.Degraded => $"Respuesta parcial: {source.LastMessage}",
            _ => $"No se pudo usar en el último intento: {source.LastMessage}"
        };
        var risk = source.Status switch
        {
            DataSourceStatus.Healthy => "Aun siendo sana, puede tener retraso, límites de plan o sesgo propio. Nunca decide sola.",
            DataSourceStatus.Degraded => "Puede estar entregando datos incompletos; baja su peso para no contaminar la señal.",
            _ => $"Acumula {source.FailureCount} fallos. Se ignora hasta que vuelva a responder o se reemplace por otra fuente."
        };
        var decision = source.Status switch
        {
            DataSourceStatus.Healthy => concept == "Market" ? "Se usa como fuente operativa o de comparación directa." : "Se usa como confirmación contextual si coincide con el activo.",
            DataSourceStatus.Degraded => "Se mantiene visible, pero solo como apoyo de baja confianza.",
            _ => "Se descarta para el análisis actual; el servicio sigue con las demás fuentes."
        };

        return new ConnectionInsight(
            extracted,
            $"Sirve para {UseForConcept(concept)}",
            obtained,
            "El sistema cruza este dato con precio, riesgo, comisiones y señales previas antes de remarcarlas.",
            GoodFor(source.Kind, concept),
            risk,
            decision);
    }

    public ConnectionInsight CatalogInsight(ConnectionCatalogItem item, string concept)
    {
        var extracted = ConceptFor(item.Group, item.Name) switch
        {
            "Market" => "Podría aportar precio, velas, volumen, liquidez o histórico de mercado para comparar contra proveedores activos.",
            "Noticias" => "Podría aportar titulares, calendario, catalizadores, regulación o sentimiento para evitar señales ciegas.",
            "IA" => "Podría aportar resumen, ranking, patrones, consenso externo o una segunda opinión sobre condiciones del mercado.",
            "Traders" => "Podría aportar perfiles, historiales, drawdown, operaciones abiertas/cerradas y consistencia de traders.",
            _ => "Podría aportar contexto complementario para enriquecer la decisión."
        };
        var risk = item.Status == "En uso"
            ? "Está activa o mapeada como útil, pero se monitorea por latencia, límites y calidad real."
            : "Aún no es una fuente principal: puede requerir llave, pago, permisos o validación legal/técnica.";
        var decision = item.Status == "En uso"
            ? "Se conserva como parte del mapa operativo."
            : "Candidata a integrar solo si mejora precisión después de costos y no agrega ruido.";

        return new ConnectionInsight(
            extracted,
            $"Sirve para {UseForConcept(concept)}",
            item.Use,
            $"{item.Requirement} Si se activa, debe registrarse telemetría, errores y utilidad real.",
            GoodForCatalog(item),
            risk,
            decision);
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

    private static string UseForConcept(string concept)
    {
        return concept switch
        {
            "Market" => "confirmar que el precio vivo, la vela y el volumen no vienen de una sola fuente aislada.",
            "Noticias" => "entender si existe un evento externo que invalida o fortalece la operación.",
            "IA" => "resumir y contrastar información sin dejar que el texto reemplace las reglas de riesgo.",
            "Traders" => "comparar el comportamiento de traders externos con las señales propias del sistema.",
            _ => "complementar el análisis sin depender de una sola entrada."
        };
    }

    private static string GoodFor(DataSourceKind kind, string concept)
    {
        return kind switch
        {
            DataSourceKind.MarketData => "Bueno para validar precio y construir gráficos; si varias fuentes coinciden, sube la confianza.",
            DataSourceKind.News or DataSourceKind.MacroReport => "Bueno para detectar catalizadores que una vela por sí sola no explica.",
            DataSourceKind.SocialSentiment => "Bueno para medir exageración del mercado, pero no basta para entrar.",
            DataSourceKind.AiAnalysis => "Bueno para resumir mucho texto y encontrar riesgos, manteniendo los números en código determinista.",
            _ => concept == "Traders" ? "Bueno para estudiar disciplina, frecuencia, drawdown y si una estrategia se sostiene." : "Bueno como respaldo contextual."
        };
    }

    private static string GoodForCatalog(ConnectionCatalogItem item)
    {
        return item.Status == "En uso"
            ? "Ya está considerada por el mapa del sistema y puede compararse contra resultados reales."
            : "Puede mejorar cobertura si aporta datos que hoy no existan en las fuentes activas.";
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

public sealed record ConnectionInsight(string Extracted, string UsedFor, string Obtained, string HowToUse, string Good, string Risk, string Decision);

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
