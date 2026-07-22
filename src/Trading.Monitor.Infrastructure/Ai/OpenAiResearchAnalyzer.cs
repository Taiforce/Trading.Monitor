using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Ai;

public sealed class OpenAiResearchAnalyzer(
    HttpClient httpClient,
    OpenAiOptions options,
    ISourceTelemetryRecorder telemetryRecorder) : IResearchAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "OpenAI research analyst";

    public async Task<IReadOnlyList<NewsItem>> AnalyzeAsync(IReadOnlyCollection<string> symbols, IReadOnlyList<NewsItem> researchItems, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return [];
        }

        var startedAt = DateTimeOffset.UtcNow;
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

        try
        {
            var prompt = BuildPrompt(symbols, researchItems);
            var payload = new
            {
                model = options.Model,
                input = prompt
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
                $"openai://responses/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                DateTimeOffset.UtcNow,
                Classify(analysis),
                symbols.Select(symbol => symbol.ToUpperInvariant()).ToArray());

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
        builder.AppendLine("Eres un analista cuantitativo de trading. Analiza el contexto reciente y devuelve un resumen breve en espanol.");
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
        builder.AppendLine("Formato de salida: maximo 8 bullets, cada bullet accionable y corto.");
        return builder.ToString();
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
