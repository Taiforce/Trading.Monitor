using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Ai;

public sealed class OpenAiResearchAnalyzer(
    HttpClient httpClient,
    OpenAiOptions options,
    ISourceTelemetryRecorder telemetryRecorder) : IResearchAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly object _cacheLock = new();
    private string _lastInputHash = "";
    private DateTimeOffset _lastAnalysisAt = DateTimeOffset.MinValue;
    private IReadOnlyList<NewsItem> _lastResult = [];

    public string Name => "OpenAI research analyst";

    public async Task<IReadOnlyList<NewsItem>> AnalyzeAsync(IReadOnlyCollection<string> symbols, IReadOnlyList<NewsItem> researchItems, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return [];
        }

        var startedAt = DateTimeOffset.UtcNow;
        var selectedResearch = SelectResearch(symbols, researchItems);
        var minimumNewsItems = Math.Clamp(options.MinimumNewsItemsToAnalyze, 0, 50);

        if (selectedResearch.Count < minimumNewsItems)
        {
            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.AiAnalysis,
                DataSourceStatus.Healthy,
                options.BaseUrl,
                $"OpenAI skipped: only {selectedResearch.Count} material news items; minimum is {minimumNewsItems}.",
                startedAt,
                DateTimeOffset.UtcNow,
                selectedResearch.Count), cancellationToken);

            return [];
        }

        var inputHash = ComputeInputHash(symbols, selectedResearch);
        if (TryUseCachedResult(inputHash, out var cachedResult, out var cacheReason))
        {
            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.AiAnalysis,
                DataSourceStatus.Healthy,
                options.BaseUrl,
                cacheReason,
                startedAt,
                DateTimeOffset.UtcNow,
                cachedResult.Count), cancellationToken);

            return cachedResult;
        }

        var apiKey = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.AiAnalysis,
                DataSourceStatus.Degraded,
                options.BaseUrl,
                $"Missing environment variable {options.ApiKeyEnvironmentVariable}.",
                startedAt,
                DateTimeOffset.UtcNow,
                0), cancellationToken);

            return [];
        }

        MarkAttempt();

        try
        {
            var prompt = BuildPrompt(symbols, selectedResearch);
            var isGpt5Family = options.Model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
            var payload = new
            {
                model = options.Model,
                input = prompt,
                reasoning = isGpt5Family ? new { effort = options.ReasoningEffort } : null,
                text = isGpt5Family ? new { verbosity = options.TextVerbosity } : null
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenAI returned {(int)response.StatusCode}: {Trim(responseBody, 600)}");
            }

            var analysis = ExtractOutputText(responseBody);

            if (string.IsNullOrWhiteSpace(analysis))
            {
                await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                    Name,
                    DataSourceKind.AiAnalysis,
                    DataSourceStatus.Degraded,
                    options.BaseUrl,
                    "OpenAI response did not contain usable text.",
                    startedAt,
                    DateTimeOffset.UtcNow,
                    0), cancellationToken);

                return [];
            }

            var item = new NewsItem(
                "ChatGPT / OpenAI",
                Trim(analysis.Trim(), 2000),
                $"openai://responses/{inputHash}",
                DateTimeOffset.UtcNow,
                Classify(analysis),
                symbols.Select(symbol => symbol.ToUpperInvariant()).ToArray());

            UpdateCache(inputHash, [item]);

            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.AiAnalysis,
                DataSourceStatus.Healthy,
                options.BaseUrl,
                "OpenAI research summary generated.",
                startedAt,
                DateTimeOffset.UtcNow,
                1), cancellationToken);

            return [item];
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                Name,
                DataSourceKind.AiAnalysis,
                DataSourceStatus.Failed,
                options.BaseUrl,
                exception.Message,
                startedAt,
                DateTimeOffset.UtcNow,
                0), cancellationToken);

            return [];
        }
    }

    private string BuildPrompt(IReadOnlyCollection<string> symbols, IReadOnlyList<NewsItem> researchItems)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Eres un analista cuantitativo de trading. Analiza el contexto reciente y devuelve un resumen breve en espanol, util para filtrar entradas reales.");
        builder.AppendLine("No prometas ganancias. Senala sesgo probable, catalizadores, riesgos y si la informacion favorece LONG, SHORT o esperar.");
        builder.AppendLine("Activos monitoreados:");
        builder.AppendLine(string.Join(", ", symbols));
        builder.AppendLine();
        builder.AppendLine("Noticias y reportes recientes:");

        foreach (var item in researchItems.Take(Math.Clamp(options.MaxNewsItems, 1, 50)))
        {
            builder.Append("- ");
            builder.Append(item.PublishedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm"));
            builder.Append(" UTC | ");
            builder.Append(item.Source);
            builder.Append(" | ");
            builder.Append(item.Sentiment);
            builder.Append(" | ");
            builder.AppendLine(item.Title);
        }

        if (researchItems.Count == 0)
        {
            builder.AppendLine("- Sin noticias recientes coincidentes. Evalua solo el riesgo informativo y recomienda prudencia.");
        }

        builder.AppendLine();
        builder.AppendLine("Formato de salida: maximo 6 bullets, cada bullet accionable y corto. Evita repetir titulares.");
        return Trim(builder.ToString(), Math.Clamp(options.MaxPromptCharacters, 1000, 20000));
    }

    private IReadOnlyList<NewsItem> SelectResearch(IReadOnlyCollection<string> symbols, IReadOnlyList<NewsItem> researchItems)
    {
        var watchedSymbols = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var maxItems = Math.Clamp(options.MaxNewsItems, 1, 50);

        return researchItems
            .Where(item => item.PublishedAt >= DateTimeOffset.UtcNow.AddHours(-24))
            .Where(item => item.Symbols.Count == 0 || item.Symbols.Any(symbol => watchedSymbols.Contains(symbol)))
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Url) ? item.Title.Trim().ToUpperInvariant() : item.Url.Trim().ToUpperInvariant())
            .Select(group => group.OrderByDescending(item => item.PublishedAt).First())
            .OrderByDescending(item => item.PublishedAt)
            .Take(maxItems)
            .ToArray();
    }

    private bool TryUseCachedResult(string inputHash, out IReadOnlyList<NewsItem> result, out string reason)
    {
        lock (_cacheLock)
        {
            var now = DateTimeOffset.UtcNow;
            var minimumGap = TimeSpan.FromMinutes(Math.Clamp(options.MinimumMinutesBetweenCalls, 0, 180));
            var sameInput = options.OnlyAnalyzeWhenNewsChanged && string.Equals(_lastInputHash, inputHash, StringComparison.Ordinal);

            if (sameInput && _lastResult.Count > 0)
            {
                result = _lastResult;
                reason = "OpenAI cache reused: research input did not change.";
                return true;
            }

            if (minimumGap > TimeSpan.Zero && now - _lastAnalysisAt < minimumGap)
            {
                result = _lastResult;
                reason = $"OpenAI cache reused: waiting {minimumGap.TotalMinutes:N0} minutes between calls.";
                return true;
            }
        }

        result = [];
        reason = "";
        return false;
    }

    private void UpdateCache(string inputHash, IReadOnlyList<NewsItem> result)
    {
        lock (_cacheLock)
        {
            _lastInputHash = inputHash;
            _lastAnalysisAt = DateTimeOffset.UtcNow;
            _lastResult = result;
        }
    }

    private void MarkAttempt()
    {
        lock (_cacheLock)
        {
            _lastAnalysisAt = DateTimeOffset.UtcNow;
        }
    }

    private static string ComputeInputHash(IReadOnlyCollection<string> symbols, IReadOnlyList<NewsItem> researchItems)
    {
        var input = new StringBuilder();
        input.AppendLine(string.Join(",", symbols.Select(symbol => symbol.Trim().ToUpperInvariant()).Order(StringComparer.Ordinal)));

        foreach (var item in researchItems)
        {
            input.Append(item.PublishedAt.UtcDateTime.ToString("O"));
            input.Append('|');
            input.Append(item.Source);
            input.Append('|');
            input.Append(item.Sentiment);
            input.Append('|');
            input.Append(item.Url);
            input.Append('|');
            input.AppendLine(item.Title);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString()))).ToLowerInvariant()[..16];
    }

    private static string ExtractOutputText(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);

            if (document.RootElement.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            {
                return outputText.GetString() ?? "";
            }

            var pieces = new List<string>();
            CollectText(document.RootElement, pieces);
            return string.Join(Environment.NewLine, pieces.Where(piece => !string.IsNullOrWhiteSpace(piece)).Distinct());
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static void CollectText(JsonElement element, List<string> pieces)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("text") && property.Value.ValueKind == JsonValueKind.String)
                {
                    pieces.Add(property.Value.GetString() ?? "");
                    continue;
                }

                CollectText(property.Value, pieces);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectText(item, pieces);
            }
        }
    }

    private static NewsSentiment Classify(string text)
    {
        var positive = new[] { "long", "alcista", "favorable", "positivo", "ruptura", "entrada" };
        var negative = new[] { "short", "bajista", "riesgo", "negativo", "caida", "esperar" };

        if (positive.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return NewsSentiment.Positive;

        if (negative.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return NewsSentiment.Negative;

        return NewsSentiment.Neutral;
    }

    private static string Trim(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
