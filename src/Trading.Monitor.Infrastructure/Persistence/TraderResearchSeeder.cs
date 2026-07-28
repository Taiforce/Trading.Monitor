using Microsoft.EntityFrameworkCore;

namespace Trading.Monitor.Infrastructure.Persistence;

public static class TraderResearchSeeder
{
    public static async Task SeedAsync(TradingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await SeedSourcesAsync(dbContext, now, cancellationToken);
        await SeedProfilesAsync(dbContext, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSourcesAsync(TradingMonitorDbContext dbContext, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sources = new[]
        {
            Source("Binance", "Binance Copy Trading", "Crypto spot/futures", "https://www.binance.com/en/copy-trading", "Ranking publico; trades completos dentro de Binance.", "Desempeno historico, max drawdown, Sharpe y AUM visibles en la plataforma.", "Buena para comparar lead traders crypto; puede requerir sesion para detalles.", true),
            Source("Bybit", "Bybit Copy Trading", "Crypto futures/spot", "https://www.bybit.com/en/copyTrade", "Ranking publico; datos finos dentro de Bybit.", "Real-time PnL/ROI y leaderboard de master traders.", "Util para ver PnL reciente, pero hay que descontar slippage y comisiones.", true),
            Source("OKX", "OKX Copy Trading", "Crypto spot/futures/bots", "https://www.okx.com/copy-trading", "Ranking publico; historial profundo dentro de OKX.", "Permite ordenar lead traders por indicadores y separar spot/futures.", "Buena fuente para comparar riesgo por tipo de copia.", true),
            Source("Bitget", "Bitget Copy Trading", "Crypto spot/futures", "https://www.bitget.com/asia/copy-trading/overview", "Ranking publico; historial operativo depende de la cuenta.", "Elite traders, copiers, 30D ROI y reglas de slippage/minimo.", "Interesante para copy trading masivo, pero exige validar liquidez.", true),
            Source("BingX", "BingX Copy Trading", "Crypto spot/futures", "https://bingx.com/en/CopyTrading", "Ranking publico; detalles completos dentro de BingX.", "Social/copy trading con copy failure reasons y limites de margen.", "La propia fuente advierte que copiar traders de alto rendimiento sigue siendo riesgoso.", true),
            Source("eToro", "eToro CopyTrader", "Acciones, ETF, crypto, forex, indices", "https://www.etoro.com/copytrader/", "Perfiles publicos; operaciones detalladas dentro de eToro.", "Popular Investors con track record, nivel de riesgo y seguidores.", "Mejor para estudiar carteras y comportamiento de inversion, no scalping rapido.", true),
            Source("ZuluTrade", "ZuluTrade Leaders", "Forex, acciones, indices, commodities, crypto", "https://www.zulutrade.com/leaders", "Ranking publico; filtros avanzados dentro de ZuluTrade.", "Leaders ordenados por performance, estabilidad y comportamiento.", "Bueno para comparar consistencia; hay que validar broker, spread y latencia.", true),
            Source("Axi", "Axi Copy Trading", "Forex, acciones, indices, commodities, crypto", "https://www.axi.com/int/copy-trading-app", "App de copy trading; detalle operativo dentro de Axi.", "Permite copiar traders lideres en varios mercados; validar regulacion, spread y disponibilidad.", "Fuente util para Forex social; no asumir rendimiento futuro por ranking.", true),
            Source("Myfxbook", "Myfxbook Systems y Calendario", "Forex, divisas, macro", "https://www.myfxbook.com/", "Sistemas, calendario economico, noticias, sentimiento y herramientas FX.", "Muy util para calendario, correlacion, volatilidad, sentimiento y track records conectados.", "Excelente para contexto Forex; los sistemas deben validarse por drawdown, broker y metodo.", true),
            Source("ForexFactory", "Forex Factory Calendar y Forums", "Forex, divisas, macro", "https://www.forexfactory.com/calendar", "Calendario economico y discusiones publicas de traders.", "Alta utilidad para eventos macro y expectativas; trades individuales no siempre son verificables.", "Fuente de contexto para evitar entradas durante noticias de alto impacto.", false),
            Source("FXStreet", "FXStreet News y Analysis", "Forex, divisas, macro", "https://www.fxstreet.com/", "Noticias, analisis, calendario y datos de divisas.", "Buena cobertura macro y tecnica por par; no es historial copy-trade.", "Capa de noticias para filtrar volatilidad por bancos centrales y datos economicos.", false),
            Source("DailyFX", "DailyFX Forex News", "Forex, divisas, commodities, indices", "https://www.dailyfx.com/", "Noticias, analisis tecnico/fundamental y calendario.", "Util para sesgo macro, volatilidad y niveles tecnicos publicados.", "Fuente de lectura; no debe copiarse sin confirmacion propia.", false),
            Source("OANDA", "OANDA Forex Data/API", "Forex, divisas, metales", "https://developer.oanda.com/", "API de precios, velas, cuentas y trading si configuras cuenta/token.", "Fuente fuerte para precios y ejecucion Forex; requiere cuenta OANDA para operar.", "Integracion candidata para broker Forex real o paper trading.", true),
            Source("Darwinex", "Darwinex DARWINs", "Estrategias reguladas multi-mercado", "https://www.darwinex.com/investors", "Estrategias publicas; trades subyacentes gestionados por motor de riesgo.", "DARWIN replica estrategia con Risk Engine y objetivo de VaR mensual.", "Mas institucional; util para estudiar track records ajustados por riesgo.", true),
            Source("TradingView", "TradingView Ideas", "Crypto, acciones, forex, indices", "https://www.tradingview.com/ideas/", "Ideas publicas por activo, pais, timeframe y reputacion del autor.", "Buena para leer tesis y setups; no siempre trae ejecucion verificable.", "Util para comparar narrativa tecnica global y detectar consensos/contradicciones.", false),
            Source("MQL5", "MQL5 Signals", "Forex, indices, commodities, crypto segun broker", "https://www.mql5.com/en/signals", "Ranking publico de senales MetaTrader; copia requiere cuenta compatible.", "Muestra crecimiento, drawdown, semanas, suscriptores y riesgo por proveedor.", "Buena fuente de senales, pero hay que filtrar martingala, grid y apalancamiento extremo.", true),
            Source("NAGA", "NAGA Autocopy", "Acciones, forex, crypto, indices", "https://naga.com/autocopy", "Social/copy trading con perfiles publicos y ranking.", "Permite revisar popularidad y desempeno dentro de la plataforma.", "Agregar solo despues de validar spreads, comisiones y disponibilidad por pais.", true),
            Source("Stocktwits", "Stocktwits Trending", "Acciones USA y crypto", "https://stocktwits.com/rankings", "Sentimiento social publico por ticker y tendencias.", "No es historial trade-by-trade; sirve para momentum social.", "Util como fuente de sentimiento, no como trader para copiar sin validacion.", false),
            Source("Bitso", "Bitso Alpha y mercado MX", "Crypto MXN/LatAm", "https://blog.bitso.com/es-la/tag/bitso-alpha", "Analisis y contexto cripto regional de Bitso.", "Sirve para entender MXN, stablecoins y comportamiento regional; no publica trades de usuarios.", "Fuente mexicana/LatAm para contexto, liquidez local y eventos de mercado.", false),
            Source("GBM", "GBM Analisis e ideas", "Acciones Mexico y USA", "https://gbm.com/academy/", "Contenido educativo/analitico de mercado mexicano y acciones.", "No publica operaciones verificables de traders; util como contexto fundamental.", "Fuente Mexico para entender BMV, emisoras y educacion bursatil.", false),
            Source("ElEconomistaMX", "El Economista Mercados", "Mexico, divisas, bolsa, macro", "https://www.eleconomista.com.mx/mercados", "Noticias publicas y RSS economico mexicano.", "Contexto macro de Mexico; no es copy trading.", "Ayuda a medir noticias de peso, tasas, BMV y entorno local.", false),
            Source("Banxico", "Banco de Mexico", "Mexico macro, tasas, tipo de cambio", "https://www.banxico.org.mx/", "Indicadores, comunicados y datos macro oficiales.", "Fuente primaria para MXN, tasas, inflacion y decisiones monetarias.", "No da senales de trading, pero mejora el filtro macro para Mexico.", false)
        };

        foreach (var source in sources)
        {
            var existing = await dbContext.TraderSources.FirstOrDefaultAsync(row => row.Platform == source.Platform, cancellationToken);
            if (existing is null)
            {
                source.Id = Guid.NewGuid();
                source.CreatedAt = now;
                source.UpdatedAt = now;
                dbContext.TraderSources.Add(source);
            }
            else
            {
                existing.Name = source.Name;
                existing.Market = source.Market;
                existing.Url = source.Url;
                existing.DataAccess = source.DataAccess;
                existing.DataQuality = source.DataQuality;
                existing.Notes = source.Notes;
                existing.SupportsCopyTrading = source.SupportsCopyTrading;
                existing.UpdatedAt = now;
            }
        }
    }

    private static async Task SeedProfilesAsync(TradingMonitorDbContext dbContext, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var profiles = new[]
        {
            Profile("eToro", "rubymza", "rubymza", "https://www.etoro.com/people/rubymza", "Multi-activo", "Popular Investor", "Perfil destacado por eToro.", "Seguidores publicos altos; revisar retorno y riesgo dentro de eToro.", "Perfil publico; historial operativo completo requiere eToro.", "Candidato para estudiar cartera de largo plazo."),
            Profile("eToro", "JeppeKirkBonde", "JeppeKirkBonde", "https://www.etoro.com/people/jeppekirkbonde", "Acciones/ETF", "Popular Investor", "Perfil destacado por eToro.", "Seguidores publicos altos; revisar retorno, drawdown y asignacion.", "Perfil publico; historial operativo completo requiere eToro.", "Candidato para estudiar estilo de cartera diversificada."),
            Profile("eToro", "CPHequities", "CPHequities", "https://www.etoro.com/people/cphequities", "Acciones/ETF", "Popular Investor", "Perfil destacado por eToro.", "Uno de los perfiles de mayor visibilidad publica en eToro.", "Perfil publico; historial operativo completo requiere eToro.", "Candidato para comparar riesgo de acciones frente a crypto."),
            Profile("eToro", "NoImportan3", "NoImportan3", "https://www.etoro.com/people/noimportan3", "Multi-activo", "Popular Investor", "Aparece en discover people de eToro.", "Retorno 24M visible publicamente en discover; confirmar dentro de eToro.", "Perfil publico; historial operativo completo requiere eToro.", "Candidato para validar consistencia contra drawdown."),
            Profile("ZuluTrade", "T1 True Range Pro", "t1-true-range-pro", "https://www.zulutrade.com/leaders", "Forex/CFD/crypto segun broker", "Leader", "Aparece en ranking publico de ZuluTrade.", "Performance publica destacada; confirmar curva y estabilidad.", "Ranking publico; historial completo requiere ZuluTrade.", "Candidato para revisar estabilidad y frecuencia."),
            Profile("ZuluTrade", "T1 TOL LANGIT V10", "t1-tol-langit-v10", "https://www.zulutrade.com/leaders", "Forex/CFD/crypto segun broker", "Leader", "Aparece en ranking publico de ZuluTrade.", "Performance publica destacada; confirmar drawdown antes de copiar.", "Ranking publico; historial completo requiere ZuluTrade.", "Candidato para revisar si el retorno viene con riesgo excesivo."),
            Profile("Axi", "Axi Copy Trading Leaders", "axi-copy-trading-leaders", "https://www.axi.com/int/copy-trading-app", "Forex/CFD multi-mercado", "Copy trader", "Ranking dentro de Axi Copy Trading.", "Revisar retorno, drawdown, pares operados y broker antes de copiar.", "Detalle completo requiere Axi.", "Candidato Forex para comparar estrategias de tendencia vs rango."),
            Profile("Myfxbook", "Verified Forex Systems", "myfxbook-verified-systems", "https://www.myfxbook.com/systems", "Forex/divisas", "Sistema verificado", "Sistemas conectados a cuentas de trading.", "Priorizar sistemas verificados con drawdown bajo y track record largo.", "Historial publico parcial; validar broker, tipo de cuenta y metodo.", "Fuente fuerte para aprender de resultados reales, sin copiar a ciegas."),
            Profile("ForexFactory", "Trade Explorer Watchlist", "forexfactory-trade-explorer", "https://www.forexfactory.com/tradeexplorers", "Forex/divisas", "Trade Explorer", "Exploradores de trading publicados por usuarios.", "Sirve para revisar consistencia, drawdown y pares favoritos cuando el usuario comparte datos.", "Depende de privacidad de cada trader.", "No usar como senal directa si no hay trade verificable."),
            Profile("FXStreet", "FXStreet Analysts", "fxstreet-analysts", "https://www.fxstreet.com/analysis", "Forex/divisas", "Analistas", "Analisis macro y tecnico por par.", "Buena lectura para EURUSD, GBPUSD, USDJPY y oro; no es copy trading.", "No publica historial trade-by-trade.", "Usar como capa de contexto y confirmacion macro."),
            Profile("DailyFX", "DailyFX Analysts", "dailyfx-analysts", "https://www.dailyfx.com/forex", "Forex/divisas", "Analistas", "Analisis tecnico, fundamental y sentimiento.", "Util para medir sesgo de mercado y eventos de alto impacto.", "No publica cartera/trades verificables.", "Candidato para veto de operaciones durante noticias importantes."),
            Profile("OANDA", "OANDA Forex Lab", "oanda-forex-lab", "https://developer.oanda.com/", "Forex/divisas", "Broker/API", "Precios y ejecucion por API si conectas cuenta.", "Sirve como proveedor de datos/ejecucion, no como trader a copiar.", "Requiere cuenta OANDA para operar.", "Integracion candidata si decides operar Forex real o paper."),
            Profile("Darwinex", "DARWINs top strategies", "darwinex-top-darwins", "https://www.darwinex.com/investors", "Multi-mercado", "DARWIN", "Estrategias de traders bajo Risk Engine.", "Performance ajustada por riesgo; comparar VaR y drawdown.", "Catalogo publico; operaciones subyacentes no siempre son visibles.", "Mejor para estudiar consistencia, no copy trading de segundos."),
            Profile("Binance", "Lead Traders ranking", "binance-lead-traders", "https://www.binance.com/en/copy-trading", "BTC/ETH/altcoins", "Lead trader", "Ranking publico de lead traders crypto.", "Revisar ROI, AUM, max drawdown y Sharpe dentro de Binance.", "Ranking publico; historial completo requiere Binance.", "Priorizar spot si no tienes el activo ni futuros."),
            Profile("Bybit", "Master Traders leaderboard", "bybit-master-traders", "https://www.bybit.com/en/derivative-activity/leaderboard-master/", "Crypto futures", "Master trader", "Leaderboard publico de master traders.", "Revisar PnL de master y followers, ROI y drawdown.", "Ranking publico; historial completo requiere Bybit.", "Usar con cautela si usa apalancamiento."),
            Profile("OKX", "Lead traders ranking", "okx-lead-traders", "https://www.okx.com/copy-trading", "Crypto spot/futures", "Lead trader", "Ranking publico de OKX copy trading.", "Separar spot/futures y comparar indicadores antes de copiar.", "Ranking publico; historial completo requiere OKX.", "Buen candidato para filtros por riesgo."),
            Profile("Bitget", "Elite Traders ranking", "bitget-elite-traders", "https://www.bitget.com/asia/copy-trading/overview", "Crypto spot/futures", "Elite trader", "Ranking publico de elite traders.", "Revisar 30D ROI, copiers y reglas de slippage/minimo.", "Ranking publico; historial completo requiere Bitget.", "Validar que el par se pueda copiar y tenga liquidez."),
            Profile("BingX", "Elite Copy Traders", "bingx-elite-copy-traders", "https://bingx.com/en/CopyTrading", "Crypto spot/futures", "Elite trader", "Ranking publico de copy traders.", "Revisar margen, slippage, limites y PnL del trader.", "Ranking publico; historial completo requiere BingX.", "No asumir que rendimiento pasado se repite."),
            Profile("TradingView", "Crypto Ideas Watchlist", "tradingview-crypto-ideas", "https://www.tradingview.com/ideas/crypto/", "Crypto global", "Ideas publicas", "Autores y publicaciones visibles por activo.", "Comparar tesis tecnicas, likes y temporalidad; no equivale a trade ejecutado.", "Ideas publicas; operaciones reales no siempre verificables.", "Usar para contexto tecnico y consenso, no copiar sin confirmacion propia."),
            Profile("TradingView", "Forex Ideas Watchlist", "tradingview-forex-ideas", "https://www.tradingview.com/ideas/forex/", "Forex/divisas", "Ideas publicas", "Autores y publicaciones visibles por par.", "Comparar tesis por temporalidad, reputacion y niveles tecnicos.", "Ideas publicas; operaciones reales no siempre verificables.", "Usar como consenso tecnico, no como copia directa."),
            Profile("TradingView", "Mexico Market Ideas", "tradingview-mexico-ideas", "https://www.tradingview.com/markets/stocks-mexico/", "Acciones Mexico", "Ideas publicas", "Ideas y graficos de mercado mexicano.", "Sirve para observar emisoras mexicanas y sentimiento tecnico.", "Ideas publicas; historial trade-by-trade no garantizado.", "Fuente Mexico para futuras extensiones de acciones/BMV."),
            Profile("MQL5", "Signals Leaderboard", "mql5-signals-leaderboard", "https://www.mql5.com/en/signals", "Forex/CFD/crypto segun broker", "Signal provider", "Ranking publico de proveedores MetaTrader.", "Revisar crecimiento, drawdown, semanas y suscriptores.", "Historial detallado requiere MQL5 y broker compatible.", "Filtrar estrategias con drawdown alto, martingala o demasiada frecuencia."),
            Profile("NAGA", "Autocopy Traders Ranking", "naga-autocopy-ranking", "https://naga.com/autocopy", "Multi-activo", "Social trader", "Ranking publico de autocopy.", "Revisar rendimiento y riesgo dentro de NAGA.", "Historial completo puede requerir cuenta.", "Fuente extra para comparar copy trading fuera de crypto puro."),
            Profile("Stocktwits", "Trending Crypto Sentiment", "stocktwits-crypto-sentiment", "https://stocktwits.com/rankings", "Crypto/acciones USA", "Sentimiento social", "Trending tickers y mensajes publicos.", "Bueno para detectar euforia o miedo social.", "No es historial de trades.", "Usar como voto de sentimiento, no como trader."),
            Profile("Bitso", "Bitso Alpha Mexico/LatAm", "bitso-alpha-latam", "https://blog.bitso.com/es-la/tag/bitso-alpha", "Crypto MXN/LatAm", "Analisis regional", "Analisis cripto regional.", "Contexto de stablecoins, MXN y adopcion regional.", "No publica trades de usuarios.", "Ayuda a entender crypto desde Mexico y LatAm."),
            Profile("GBM", "GBM Ideas Mexico", "gbm-ideas-mexico", "https://gbm.com/academy/", "Acciones Mexico/USA", "Analisis educativo", "Contenido de mercado y educacion financiera.", "Contexto para acciones y portafolio, no scalping.", "No publica historial trade-by-trade.", "Fuente local para ampliar a BMV y acciones."),
            Profile("ElEconomistaMX", "Mercados Mexico Watchlist", "eleconomista-mx-mercados", "https://www.eleconomista.com.mx/mercados", "Mexico macro/bolsa", "Noticias de mercado", "Noticias de mercados, peso, bolsa y economia.", "Contexto macro y catalizadores locales.", "No publica trades.", "Usar como capa de noticias Mexico."),
            Profile("Banxico", "Macro Mexico Watchlist", "banxico-macro-watchlist", "https://www.banxico.org.mx/", "MXN/tasas/macro", "Fuente oficial", "Banco central mexicano.", "Tasas, tipo de cambio, inflacion y comunicados oficiales.", "No publica trades.", "Filtro macro para evitar senales durante eventos sensibles.")
        };

        foreach (var profile in profiles)
        {
            var existing = await dbContext.TraderProfiles.FirstOrDefaultAsync(row => row.Platform == profile.Platform && row.ExternalId == profile.ExternalId, cancellationToken);
            if (existing is null)
            {
                profile.Id = Guid.NewGuid();
                profile.CreatedAt = now;
                profile.UpdatedAt = now;
                dbContext.TraderProfiles.Add(profile);
            }
            else
            {
                existing.DisplayName = profile.DisplayName;
                existing.ProfileUrl = profile.ProfileUrl;
                existing.Market = profile.Market;
                existing.StrategyType = profile.StrategyType;
                existing.PopularityText = profile.PopularityText;
                existing.PerformanceText = profile.PerformanceText;
                existing.DataAvailability = profile.DataAvailability;
                existing.Notes = profile.Notes;
                existing.UpdatedAt = now;
            }
        }
    }

    private static TraderSourceEntity Source(string platform, string name, string market, string url, string dataAccess, string dataQuality, string notes, bool supportsCopyTrading)
    {
        return new TraderSourceEntity
        {
            Platform = platform,
            Name = name,
            Market = market,
            Url = url,
            DataAccess = dataAccess,
            DataQuality = dataQuality,
            Notes = notes,
            SupportsCopyTrading = supportsCopyTrading
        };
    }

    private static TraderProfileEntity Profile(string platform, string displayName, string externalId, string profileUrl, string market, string strategyType, string popularityText, string performanceText,
        string dataAvailability, string notes)
    {
        return new TraderProfileEntity
        {
            Platform = platform,
            DisplayName = displayName,
            ExternalId = externalId,
            ProfileUrl = profileUrl,
            Market = market,
            StrategyType = strategyType,
            PopularityText = popularityText,
            PerformanceText = performanceText,
            DataAvailability = dataAvailability,
            Notes = notes
        };
    }
}
