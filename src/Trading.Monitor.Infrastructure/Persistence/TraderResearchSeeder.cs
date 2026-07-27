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
            Source("eToro", "eToro CopyTrader", "Acciones, ETF, crypto, indices", "https://www.etoro.com/copytrader/", "Perfiles publicos; operaciones detalladas dentro de eToro.", "Popular Investors con track record, nivel de riesgo y seguidores.", "Mejor para estudiar carteras y comportamiento de inversion, no scalping rapido.", true),
            Source("ZuluTrade", "ZuluTrade Leaders", "Forex, acciones, indices, commodities, crypto", "https://www.zulutrade.com/leaders", "Ranking publico; filtros avanzados dentro de ZuluTrade.", "Leaders ordenados por performance, estabilidad y comportamiento.", "Bueno para comparar consistencia; hay que validar broker, spread y latencia.", true),
            Source("Darwinex", "Darwinex DARWINs", "Estrategias reguladas multi-mercado", "https://www.darwinex.com/investors", "Estrategias publicas; trades subyacentes gestionados por motor de riesgo.", "DARWIN replica estrategia con Risk Engine y objetivo de VaR mensual.", "Mas institucional; util para estudiar track records ajustados por riesgo.", true)
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
            Profile("Darwinex", "DARWINs top strategies", "darwinex-top-darwins", "https://www.darwinex.com/investors", "Multi-mercado", "DARWIN", "Estrategias de traders bajo Risk Engine.", "Performance ajustada por riesgo; comparar VaR y drawdown.", "Catalogo publico; operaciones subyacentes no siempre son visibles.", "Mejor para estudiar consistencia, no copy trading de segundos."),
            Profile("Binance", "Lead Traders ranking", "binance-lead-traders", "https://www.binance.com/en/copy-trading", "BTC/ETH/altcoins", "Lead trader", "Ranking publico de lead traders crypto.", "Revisar ROI, AUM, max drawdown y Sharpe dentro de Binance.", "Ranking publico; historial completo requiere Binance.", "Priorizar spot si no tienes el activo ni futuros."),
            Profile("Bybit", "Master Traders leaderboard", "bybit-master-traders", "https://www.bybit.com/en/derivative-activity/leaderboard-master/", "Crypto futures", "Master trader", "Leaderboard publico de master traders.", "Revisar PnL de master y followers, ROI y drawdown.", "Ranking publico; historial completo requiere Bybit.", "Usar con cautela si usa apalancamiento."),
            Profile("OKX", "Lead traders ranking", "okx-lead-traders", "https://www.okx.com/copy-trading", "Crypto spot/futures", "Lead trader", "Ranking publico de OKX copy trading.", "Separar spot/futures y comparar indicadores antes de copiar.", "Ranking publico; historial completo requiere OKX.", "Buen candidato para filtros por riesgo."),
            Profile("Bitget", "Elite Traders ranking", "bitget-elite-traders", "https://www.bitget.com/asia/copy-trading/overview", "Crypto spot/futures", "Elite trader", "Ranking publico de elite traders.", "Revisar 30D ROI, copiers y reglas de slippage/minimo.", "Ranking publico; historial completo requiere Bitget.", "Validar que el par se pueda copiar y tenga liquidez."),
            Profile("BingX", "Elite Copy Traders", "bingx-elite-copy-traders", "https://bingx.com/en/CopyTrading", "Crypto spot/futures", "Elite trader", "Ranking publico de copy traders.", "Revisar margen, slippage, limites y PnL del trader.", "Ranking publico; historial completo requiere BingX.", "No asumir que rendimiento pasado se repite.")
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
