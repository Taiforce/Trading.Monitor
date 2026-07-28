using System.Xml.Linq;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.News;

public sealed class RssNewsProvider(HttpClient httpClient, NewsOptions options, ISourceTelemetryRecorder telemetryRecorder) : INewsProvider
{
    private static readonly string[] PositiveWords =
    [
        "approval", "approved", "bullish", "rally", "surge", "breakout", "inflow", "adoption", "partnership", "upgrade",
        "record high", "soars", "gains", "positive", "launch", "accumulates", "buys", "beat",
        "aprobacion", "aprobación", "aprobado", "alcista", "repunte", "sube", "suben", "gana", "ganan", "avance", "avanza",
        "rompe resistencia", "maximo", "máximo", "maximos", "máximos", "entrada", "adopcion", "adopción", "alianza", "mejora", "compra", "compras"
    ];

    private static readonly string[] NegativeWords =
    [
        "hack", "lawsuit", "ban", "bearish", "outflow", "falls", "drops", "selloff", "liquidation", "probe",
        "exploit", "bankruptcy", "downgrade", "crackdown", "negative", "reject", "rejected", "fraud", "breach",
        "cae", "caen", "baja", "bajan", "perdida", "pérdida", "perdidas", "pérdidas", "bajista", "venta", "ventas", "demanda",
        "prohibicion", "prohibición", "rechazo", "fraude", "hackeo", "quiebra", "liquidacion", "liquidación", "investigacion", "investigación", "presion", "presión"
    ];

    public string Name => "RSS research feeds";

    public async Task<IReadOnlyList<NewsItem>> GetLatestAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        var items = new List<NewsItem>();
        var maxAge = TimeSpan.FromHours(options.MaxNewsAgeHours);
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge);

        foreach (var feed in options.Feeds.Where(feed => !string.IsNullOrWhiteSpace(feed)))
        {
            var startedAt = DateTimeOffset.UtcNow;
            var source = ResolveFallbackSource(feed);

            try
            {
                var xml = await httpClient.GetStringAsync(feed, cancellationToken);
                var document = XDocument.Parse(xml);
                source = ResolveSource(document, feed);
                var feedItems = new List<NewsItem>();

                foreach (var node in FindEntries(document))
                {
                    var title = ReadValue(node, "title");

                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    var publishedAt = ParsePublishedAt(node);

                    if (publishedAt < cutoff)
                        continue;

                    var matchedSymbols = ResolveSymbols(title, symbols);

                    if (matchedSymbols.Count == 0)
                        continue;

                    feedItems.Add(new NewsItem(source, title.Trim(), ResolveUrl(node), publishedAt, Classify(title), matchedSymbols));
                }

                items.AddRange(feedItems);
                await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                    source,
                    DataSourceKind.News,
                    feedItems.Count > 0 ? DataSourceStatus.Healthy : DataSourceStatus.Degraded,
                    feed,
                    $"{feedItems.Count} matching research items.",
                    startedAt,
                    DateTimeOffset.UtcNow,
                    feedItems.Count), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                await telemetryRecorder.RecordAsync(new DataSourceHealthEvent(
                    source,
                    DataSourceKind.News,
                    DataSourceStatus.Failed,
                    feed,
                    exception.Message,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    0), cancellationToken);
            }
        }

        return items.GroupBy(item => item.Url.Length > 0 ? item.Url : item.Title, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).OrderByDescending(item => item.PublishedAt).ToArray();
    }

    private IReadOnlyList<string> ResolveSymbols(string text, IReadOnlyCollection<string> symbols)
    {
        var matches = new List<string>();

        foreach (var symbol in symbols)
        {
            var keywords = options.SymbolKeywords.TryGetValue(symbol, out var configured) ? configured : [symbol, symbol.Replace("USDT", "", StringComparison.OrdinalIgnoreCase)];

            if (keywords.Any(keyword => ContainsWord(text, keyword)))
                matches.Add(symbol);
        }

        return matches;
    }

    private static IEnumerable<XElement> FindEntries(XDocument document)
    {
        var rssItems = document.Descendants().Where(element => element.Name.LocalName == "item").ToArray();
        return rssItems.Length > 0 ? rssItems : document.Descendants().Where(element => element.Name.LocalName == "entry");
    }

    private static string ResolveSource(XDocument document, string feed)
    {
        var title = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "channel")?.Elements().FirstOrDefault(element => element.Name.LocalName == "title")?.Value;

        if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();

        return Uri.TryCreate(feed, UriKind.Absolute, out var uri) ? uri.Host : feed;
    }

    private static string ResolveFallbackSource(string feed)
    {
        return Uri.TryCreate(feed, UriKind.Absolute, out var uri) ? uri.Host : feed;
    }

    private static string ReadValue(XElement node, string localName)
    {
        return node.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value ?? "";
    }

    private static DateTimeOffset ParsePublishedAt(XElement node)
    {
        var value = ReadValue(node, "pubDate");

        if (string.IsNullOrWhiteSpace(value))
            value = ReadValue(node, "published");

        if (string.IsNullOrWhiteSpace(value))
            value = ReadValue(node, "updated");

        return DateTimeOffset.TryParse(value, out var publishedAt) ? publishedAt.ToUniversalTime() : DateTimeOffset.UtcNow;
    }

    private static string ResolveUrl(XElement node)
    {
        var link = node.Elements().FirstOrDefault(element => element.Name.LocalName == "link");

        if (link is null)
            return "";

        var href = link.Attribute("href")?.Value;
        return string.IsNullOrWhiteSpace(href) ? link.Value.Trim() : href.Trim();
    }

    private static NewsSentiment Classify(string title)
    {
        if (PositiveWords.Any(word => ContainsWord(title, word)))
            return NewsSentiment.Positive;

        if (NegativeWords.Any(word => ContainsWord(title, word)))
            return NewsSentiment.Negative;

        return NewsSentiment.Neutral;
    }

    private static bool ContainsWord(string text, string word)
    {
        return text.Contains(word, StringComparison.OrdinalIgnoreCase);
    }
}
