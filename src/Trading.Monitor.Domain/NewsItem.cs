namespace Trading.Monitor.Domain;

public sealed record NewsItem(string Source, string Title, string Url, DateTimeOffset PublishedAt, NewsSentiment Sentiment, IReadOnlyList<string> Symbols);