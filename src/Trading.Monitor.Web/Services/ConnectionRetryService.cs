using System.Net;
using System.Net.Sockets;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Services;

public sealed class ConnectionRetryService(HttpClient httpClient, ISourceTelemetryRecorder telemetryRecorder, ILogger<ConnectionRetryService> logger)
{
    /// <summary>
    /// Hostname suffixes for market/news/AI providers this dashboard is allowed to reach directly.
    /// Anything else (including raw IP literals, cloud metadata, or intranet hosts) is rejected
    /// server-side to prevent SSRF via an operator-supplied "retry connection" URL.
    /// </summary>
    private static readonly string[] AllowedHostSuffixes =
    [
        "binance.com", "binance.us", "coinbase.com", "kraken.com", "yahoo.com", "alphavantage.co",
        "cryptopanic.com", "alternative.me", "openai.com", "telegram.org"
    ];

    public async Task<ConnectionRetryResult> RetryAsync(ConnectionRetryRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sourceName = string.IsNullOrWhiteSpace(request.SourceName) ? "Fuente sin nombre" : request.SourceName.Trim();
        var kind = ResolveKind(request.Kind, sourceName);
        var url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim();
        DataSourceStatus status;
        string message;

        try
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            {
                status = DataSourceStatus.Degraded;
                message = "La fuente no tiene una URL HTTPS directa para probar; se mantiene mapeada, pero requiere integración específica.";
            }
            else if (!await IsAllowedPublicHostAsync(uri, cancellationToken))
            {
                status = DataSourceStatus.Degraded;
                message = "La URL no corresponde a un proveedor externo permitido; no se realizó ninguna solicitud.";
                logger.LogWarning("Rejected connection retry to disallowed host {Host} for source {SourceName}.", uri.Host, sourceName);
            }
            else
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                status = response.IsSuccessStatusCode ? DataSourceStatus.Healthy : DataSourceStatus.Failed;
                message = response.IsSuccessStatusCode
                    ? $"Respondió {(int)response.StatusCode} {response.ReasonPhrase}. La conexión puede seguir usándose como fuente disponible."
                    : $"Respondió {(int)response.StatusCode} {response.ReasonPhrase}. Se baja su confianza y el sistema sigue con otras fuentes.";
            }
        }
        catch (Exception exception)
        {
            status = DataSourceStatus.Failed;
            message = "No se pudo completar el reintento de conexión.";
            logger.LogWarning(exception, "Connection retry failed for {SourceName}.", sourceName);
        }

        var completedAt = DateTimeOffset.UtcNow;
        await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(sourceName, kind, status, url, message, startedAt, completedAt, status == DataSourceStatus.Healthy ? 1 : 0), cancellationToken);

        return new ConnectionRetryResult(sourceName, kind.ToString(), status.ToString(), StatusCss(status), message, completedAt);
    }

    private static async Task<bool> IsAllowedPublicHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var host = uri.Host;

        if (!AllowedHostSuffixes.Any(suffix => host.Equals(suffix, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (IPAddress.TryParse(host, out _))
            return false;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Length > 0 && addresses.All(IsPublicAddress);
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return false;

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return !address.IsIPv6LinkLocal;

        var bytes = address.GetAddressBytes();

        // RFC1918 private ranges, loopback, link-local (incl. 169.254.169.254 cloud metadata), CGNAT.
        return bytes[0] switch
        {
            10 => false,
            127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 168 => false,
            100 when bytes[1] is >= 64 and <= 127 => false,
            0 => false,
            _ => true
        };
    }

    private static DataSourceKind ResolveKind(string? kind, string sourceName)
    {
        if (Enum.TryParse<DataSourceKind>(kind, true, out var parsed))
            return parsed;

        var text = $"{kind} {sourceName}";
        if (ContainsAny(text, "market", "mercado", "binance", "coinbase", "kraken", "yahoo", "oanda"))
            return DataSourceKind.MarketData;

        if (ContainsAny(text, "ia", "ai", "openai", "kensho", "tickeron", "trendspider"))
            return DataSourceKind.AiAnalysis;

        if (ContainsAny(text, "sentimiento", "fear", "social"))
            return DataSourceKind.SocialSentiment;

        if (ContainsAny(text, "macro", "fred", "fed", "banxico", "forex"))
            return DataSourceKind.MacroReport;

        if (ContainsAny(text, "noticia", "news", "rss"))
            return DataSourceKind.News;

        return DataSourceKind.Research;
    }

    private static string StatusCss(DataSourceStatus status)
    {
        return status switch
        {
            DataSourceStatus.Healthy => "status-win",
            DataSourceStatus.Failed => "status-loss",
            _ => "status-muted"
        };
    }

    private static bool ContainsAny(string value, params string[] patterns)
    {
        return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record ConnectionRetryRequest(string SourceName, string? Kind, string? Url, string? Scope);

public sealed record ConnectionRetryResult(string SourceName, string Kind, string StatusLabel, string CssClass, string Message, DateTimeOffset CheckedAt);
