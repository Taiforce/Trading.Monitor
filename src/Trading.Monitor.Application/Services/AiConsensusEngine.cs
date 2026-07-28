using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed class AiConsensusEngine
{
    private static readonly string[] PositiveWords =
    [
        "alcista", "bullish", "compra", "compras", "positivo", "fuerte", "soporte", "ruptura", "momentum",
        "volumen", "liquidez", "acumulacion", "acumulación", "adopcion", "adopción", "entrada", "rebote"
    ];

    private static readonly string[] NegativeWords =
    [
        "bajista", "bearish", "venta", "ventas", "negativo", "debil", "débil", "resistencia", "rechazo",
        "miedo", "riesgo", "regulacion", "regulación", "hack", "liquidacion", "liquidación", "caida", "caída"
    ];

    private static readonly string[] TechnicalWords =
    [
        "ema", "sma", "rsi", "macd", "vwap", "atr", "adx", "bollinger", "soporte", "resistencia",
        "volumen", "ruptura", "tendencia", "order book", "liquidez"
    ];

    private static readonly string[] ResearchWords =
    [
        "noticia", "news", "macro", "fed", "inflacion", "inflación", "etf", "earnings", "reporte",
        "sentimiento", "regulacion", "regulación", "sector", "on-chain", "whale", "ballena"
    ];

    private static readonly string[] AlternativeDataWords =
    [
        "social", "x ", "twitter", "reddit", "foro", "foros", "tendencia", "popularidad", "busqueda",
        "búsqueda", "mencion", "mención", "sentimiento", "influencer", "comunidad"
    ];

    public AiConsensusResult Evaluate(OpportunityReportRow row, IReadOnlyCollection<OpportunityReportRow> context)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(context);

        var models = new List<AiInspiredModelScore>
        {
            BuildJorgAi(row),
            BuildZellaAi(row, context),
            BuildHollyAi(row),
            BuildTrendSpider(row),
            BuildTickeron(row),
            BuildComposer(row),
            BuildIntellectia(row),
            BuildStockGeist(row),
            BuildKensho(row),
            BuildZywave(row),
            BuildTickerTags(row),
            BuildSentifi(row),
            BuildQAi(row)
        };

        models.Add(BuildNumerai(row, models));

        var vetoReasons = BuildVetoReasons(row, models).ToArray();
        var compositeScore = BuildCompositeScore(models, vetoReasons);
        var label = BuildConsensusLabel(compositeScore, vetoReasons.Length);
        var readout = BuildPlainReadout(row, compositeScore, label, vetoReasons);

        return new AiConsensusResult(row.Id, row.Symbol, compositeScore, label, readout, models, vetoReasons);
    }

    private static AiInspiredModelScore BuildJorgAi(OpportunityReportRow row)
    {
        var score = WeightedAverage(
            (row.Score, 1.1m),
            (RiskRewardScore(row), 1m),
            (ConfirmationScore(row), 0.9m),
            (NetEdgeScore(row), 1.2m));

        return Model("JorgAI", "Automatización supervisada", score,
            $"Ensamble de entrada: score {row.Score}/100, {ConfirmationCount(row)} marcos alineados y R:B 1:{row.RiskReward:N2}.");
    }

    private static AiInspiredModelScore BuildZellaAi(OpportunityReportRow row, IReadOnlyCollection<OpportunityReportRow> context)
    {
        var peers = context
            .Where(item => item.Status != OpportunityStatus.Open)
            .Where(item => string.Equals(item.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase) && item.Side == row.Side)
            .ToArray();

        if (peers.Length == 0)
            return Model("TradeZella / Zella AI", "Diario y aprendizaje", 58,
                "Aún no hay suficientes cierres parecidos; se queda neutral hasta tener historial real.");

        var winners = peers.Count(item => item.RealizedNetPnL > 0m);
        var winRate = (decimal)winners / peers.Length * 100m;
        var netPercent = peers.Sum(item => item.RealizedNetPnL ?? 0m) / Math.Max(1m, row.Capital * peers.Length) * 100m;
        var score = ClampScore(30m + winRate * 0.45m + Math.Clamp(netPercent * 3m, -18m, 25m));

        return Model("TradeZella / Zella AI", "Diario y aprendizaje", score,
            $"{peers.Length} operaciones similares cerradas: {winRate:N1}% ganadoras y {netPercent:N2}% neto promedio.");
    }

    private static AiInspiredModelScore BuildHollyAi(OpportunityReportRow row)
    {
        var hasCompletePlan = row.EntryPrice > 0m && row.StopLoss > 0m && row.TakeProfit1 > 0m;
        var score = WeightedAverage(
            (row.Score, 1.2m),
            (hasCompletePlan ? 86 : 38, 0.9m),
            (RiskRewardScore(row), 1m),
            (row.Status == OpportunityStatus.Open ? 74 : 62, 0.5m));

        return Model("Trade Ideas / Holly AI", "Entrada y salida claras", score,
            hasCompletePlan ? "Tiene entrada, salida de ganancia y corte de pérdida definidos." : "La señal no tiene un plan completo.");
    }

    private static AiInspiredModelScore BuildTrendSpider(OpportunityReportRow row)
    {
        var text = CombinedText(row);
        var technicalHits = CountMatches(text, TechnicalWords);
        var score = WeightedAverage(
            (ConfirmationScore(row), 1.2m),
            (ClampScore(45m + technicalHits * 8m), 1m),
            (RiskRewardScore(row), 0.8m));

        return Model("TrendSpider", "Confluencia técnica", score,
            $"{technicalHits} pistas técnicas detectadas y {ConfirmationCount(row)} temporalidades confirmando.");
    }

    private static AiInspiredModelScore BuildTickeron(OpportunityReportRow row)
    {
        var intervals = SplitParts(row.ConfirmingIntervals);
        var fastTimeframes = intervals.Count(item => item.Contains("1m", StringComparison.OrdinalIgnoreCase)
                                                     || item.Contains("5m", StringComparison.OrdinalIgnoreCase)
                                                     || item.Contains("15m", StringComparison.OrdinalIgnoreCase)
                                                     || item.Contains("60m", StringComparison.OrdinalIgnoreCase)
                                                     || item.Contains("1h", StringComparison.OrdinalIgnoreCase));
        var score = WeightedAverage(
            (row.Score, 1m),
            (ClampScore(48m + fastTimeframes * 13m), 1m),
            (NetEdgeScore(row), 1m));

        return Model("Tickeron", "Agentes por temporalidad", score,
            fastTimeframes > 0 ? $"{fastTimeframes} marcos rápidos/intradía apoyan la idea." : "Poca evidencia de marcos rápidos para una entrada táctica.");
    }

    private static AiInspiredModelScore BuildComposer(OpportunityReportRow row)
    {
        var reasonCount = SplitParts(row.Reasons).Count;
        var riskCount = SplitParts(row.Risks).Count;
        var completeRules = row.EntryPrice > 0m && row.StopLoss > 0m && row.TakeProfit1 > 0m && row.ExpiresAt > row.ObservedAt;
        var score = WeightedAverage(
            (completeRules ? 86 : 42, 1.2m),
            (ClampScore(48m + reasonCount * 7m - riskCount * 5m), 1m),
            (RiskRewardScore(row), 0.8m));

        return Model("Composer", "Reglas probables de backtest", score,
            completeRules ? "La operación puede explicarse como reglas medibles." : "Faltan datos para convertirla en estrategia repetible.");
    }

    private static AiInspiredModelScore BuildIntellectia(OpportunityReportRow row)
    {
        var text = CombinedText(row);
        var researchHits = CountMatches(text, ResearchWords);
        var score = WeightedAverage(
            (row.Score, 0.9m),
            (ClampScore(50m + researchHits * 9m), 1.1m),
            (NetEdgeScore(row), 0.8m));

        return Model("Intellectia", "Investigación y contexto", score,
            researchHits > 0 ? $"{researchHits} pistas de contexto/noticias encontradas." : "No se ve apoyo claro de noticias o contexto amplio.");
    }

    private static AiInspiredModelScore BuildStockGeist(OpportunityReportRow row)
    {
        var text = CombinedText(row);
        var sentiment = SentimentBalance(text);
        var score = ClampScore(58m + sentiment * 8m + (row.Score - 70m) * 0.25m);

        return Model("StockGeist", "Sentimiento social/noticias", score,
            sentiment switch
            {
                > 0 => "El texto disponible se inclina positivo.",
                < 0 => "El texto disponible trae mas alertas negativas que positivas.",
                _ => "Sentimiento neutral o insuficiente."
            });
    }

    private static AiInspiredModelScore BuildKensho(OpportunityReportRow row)
    {
        var evidence = ConfirmationCount(row) + SplitParts(row.Reasons).Count;
        var riskPenalty = Math.Min(30, SplitParts(row.Risks).Count * 7);
        var score = ClampScore(42m + evidence * 6m + row.Score * 0.25m - riskPenalty);

        return Model("Kensho", "Datos confiables y trazables", score,
            $"Evidencia trazable: {evidence} puntos; penalizacion por riesgos: {riskPenalty}.");
    }

    private static AiInspiredModelScore BuildZywave(OpportunityReportRow row)
    {
        var targetPercent = TargetNetPercent(row);
        var lossPercent = LossPercent(row);
        var score = ClampScore(55m + row.RiskReward * 12m + targetPercent * 8m - lossPercent * 13m);

        return Model("Zywave", "Benchmark de riesgo", score,
            $"Ganancia objetivo {targetPercent:N2}% contra pérdida máxima {lossPercent:N2}% después de comisiones.");
    }

    private static AiInspiredModelScore BuildTickerTags(OpportunityReportRow row)
    {
        var text = CombinedText(row);
        var tags = CountMatches(text, AlternativeDataWords.Concat(ResearchWords).ToArray());
        var score = WeightedAverage(
            (ClampScore(50m + tags * 7m + SentimentBalance(text) * 5m), 1m),
            (row.Score, 0.55m));

        return Model("TickerTags", "Datos alternativos y temas", score,
            tags > 0 ? $"{tags} temas externos detectados en razones/riesgos." : "Sin temas sociales o alternativos visibles en esta señal.");
    }

    private static AiInspiredModelScore BuildSentifi(OpportunityReportRow row)
    {
        var text = CombinedText(row);
        var socialHits = CountMatches(text, AlternativeDataWords);
        var sentiment = SentimentBalance(text);
        var score = ClampScore(54m + socialHits * 8m + sentiment * 7m + (row.Score - 75m) * 0.2m);

        return Model("Sentifi", "Señales sociales calificadas", score,
            socialHits > 0 ? $"{socialHits} señales sociales/noticiosas apoyan el contexto." : "No hay señal social fuerte disponible.");
    }

    private static AiInspiredModelScore BuildQAi(OpportunityReportRow row)
    {
        var lossPercent = LossPercent(row);
        var targetPercent = TargetNetPercent(row);
        var spotFriendly = row.Side == MarketSide.Long ? 8m : -10m;
        var score = ClampScore(58m + spotFriendly + targetPercent * 7m - lossPercent * 9m + Math.Min(row.RiskReward * 6m, 18m));

        return Model("Q.ai", "Cartera y protección", score,
            row.Side == MarketSide.Long
                ? "Más compatible con una wallet spot: comprar primero y vender después."
                : "Requiere moneda, margen o futuros; no conviene activarla si no tienes ese activo.");
    }

    private static AiInspiredModelScore BuildNumerai(OpportunityReportRow row, IReadOnlyList<AiInspiredModelScore> previousModels)
    {
        var average = previousModels.Average(model => model.Score);
        var dispersion = previousModels.Max(model => model.Score) - previousModels.Min(model => model.Score);
        var strongVotes = previousModels.Count(model => model.Score >= 70);
        var weakVotes = previousModels.Count(model => model.Score < 50);
        var score = ClampScore((decimal)average - (decimal)dispersion * 0.22m + strongVotes * 2m - weakVotes * 3m);

        return Model("Numerai", "Meta-modelo de consenso", score,
            $"{strongVotes} modelos apoyan la señal; dispersión {dispersion:N0}. Menos dispersión significa más acuerdo.");
    }

    private static IReadOnlyList<string> BuildVetoReasons(OpportunityReportRow row, IReadOnlyList<AiInspiredModelScore> models)
    {
        var vetoes = new List<string>();
        var targetPercent = TargetNetPercent(row);
        var lossPercent = LossPercent(row);

        if (row.NetProfitAtTakeProfit1 <= 0m || targetPercent <= 0m)
            vetoes.Add("La ganancia objetivo no deja dinero neto después de comisiones.");

        if (row.RiskReward < 1.35m)
            vetoes.Add($"Riesgo/beneficio bajo: 1:{row.RiskReward:N2}.");

        if (lossPercent > targetPercent * 1.35m && lossPercent > 0.35m)
            vetoes.Add($"La pérdida posible ({lossPercent:N2}%) pesa más que la ganancia objetivo ({targetPercent:N2}%).");

        if (row.Score < 72)
            vetoes.Add($"Score base bajo: {row.Score}/100.");

        if (ConfirmationCount(row) < 2)
            vetoes.Add("Pocas temporalidades confirmando.");

        if (row.Status != OpportunityStatus.Open && (row.RealizedNetPnL ?? 0m) <= 0m)
            vetoes.Add("Histórico cerrado sin ganancia real; usar solo para aprendizaje.");

        if (models.Count(model => model.Score >= 70) < 5)
            vetoes.Add("Menos de 5 modelos del consenso apoyan la operación.");

        return vetoes;
    }

    private static int BuildCompositeScore(IReadOnlyList<AiInspiredModelScore> models, IReadOnlyList<string> vetoReasons)
    {
        var weighted = models.Select(model =>
        {
            var weight = model.Name switch
            {
                "Numerai" => 1.25m,
                "Zywave" => 1.15m,
                "Kensho" => 1.1m,
                "TradeZella / Zella AI" => 1.05m,
                "JorgAI" => 1.05m,
                _ => 1m
            };

            return (model.Score, weight);
        }).ToArray();

        return ClampScore(WeightedAverageRaw(weighted) - vetoReasons.Count * 7m);
    }

    private static string BuildConsensusLabel(int compositeScore, int vetoCount)
    {
        if (vetoCount > 0)
            return "Esperar";

        return compositeScore switch
        {
            >= 85 => "Muy fuerte, no garantizada",
            >= 75 => "Fuerte, revisar precio vivo",
            >= 65 => "Vigilable",
            _ => "Esperar"
        };
    }

    private static string BuildPlainReadout(OpportunityReportRow row, int compositeScore, string label, IReadOnlyList<string> vetoReasons)
    {
        if (row.Status != OpportunityStatus.Open)
        {
            var result = row.RealizedNetPnL.HasValue ? $"{row.RealizedNetPnL.Value:C2}" : "sin cierre neto";
            return $"Histórico para aprender: cerró en {result}. Consenso {compositeScore}/100.";
        }

        if (vetoReasons.Count > 0)
            return $"Esperar: {vetoReasons[0]} Consenso {compositeScore}/100.";

        return $"{label}: entrada posible solo si el precio vivo sigue dentro del plan y la ganancia neta supera comisiones. Consenso {compositeScore}/100.";
    }

    private static AiInspiredModelScore Model(string name, string inspiration, int score, string why)
    {
        var signal = score switch
        {
            >= 75 => "Apoya",
            >= 60 => "Observa",
            >= 45 => "Duda",
            _ => "Bloquea"
        };

        return new AiInspiredModelScore(name, inspiration, score, signal, why);
    }

    private static int RiskRewardScore(OpportunityReportRow row)
    {
        return ClampScore(32m + row.RiskReward * 19m);
    }

    private static int ConfirmationScore(OpportunityReportRow row)
    {
        return ClampScore(38m + ConfirmationCount(row) * 14m);
    }

    private static int NetEdgeScore(OpportunityReportRow row)
    {
        var targetPercent = TargetNetPercent(row);
        var lossPercent = LossPercent(row);
        var edgeRatio = lossPercent <= 0m ? targetPercent : targetPercent / lossPercent;

        return ClampScore(44m + targetPercent * 14m + edgeRatio * 18m);
    }

    private static decimal TargetNetPercent(OpportunityReportRow row)
    {
        return row.Capital <= 0m ? 0m : row.NetProfitAtTakeProfit1 / row.Capital * 100m;
    }

    private static decimal LossPercent(OpportunityReportRow row)
    {
        return row.Capital <= 0m ? 0m : Math.Abs(row.NetLossAtStop) / row.Capital * 100m;
    }

    private static int ConfirmationCount(OpportunityReportRow row)
    {
        return SplitParts(row.ConfirmingIntervals).Count;
    }

    private static IReadOnlyList<string> SplitParts(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string CombinedText(OpportunityReportRow row)
    {
        return $"{row.Symbol} {row.ConfirmingIntervals} {row.Reasons} {row.Risks}".ToLowerInvariant();
    }

    private static int SentimentBalance(string text)
    {
        return CountMatches(text, PositiveWords) - CountMatches(text, NegativeWords);
    }

    private static int CountMatches(string text, IReadOnlyCollection<string> words)
    {
        return words.Count(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static int WeightedAverage(params (int Score, decimal Weight)[] scores)
    {
        return ClampScore(WeightedAverageRaw(scores));
    }

    private static decimal WeightedAverageRaw(IReadOnlyCollection<(int Score, decimal Weight)> scores)
    {
        var weight = scores.Sum(item => item.Weight);
        if (weight <= 0m)
            return 0m;

        return scores.Sum(item => item.Score * item.Weight) / weight;
    }

    private static int ClampScore(decimal value)
    {
        return (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0m, 100m);
    }
}

public sealed record AiConsensusResult(
    Guid OpportunityId,
    string Symbol,
    int CompositeScore,
    string ConsensusLabel,
    string PlainReadout,
    IReadOnlyList<AiInspiredModelScore> Models,
    IReadOnlyList<string> VetoReasons)
{
    public bool HasVeto => VetoReasons.Count > 0;

    public IReadOnlyList<AiInspiredModelScore> TopModels => Models
        .OrderByDescending(model => model.Score)
        .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
        .Take(3)
        .ToArray();
}

public sealed record AiInspiredModelScore(string Name, string Inspiration, int Score, string Signal, string Why);
