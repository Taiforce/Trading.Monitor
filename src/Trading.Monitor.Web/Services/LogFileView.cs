namespace Trading.Monitor.Web.Services;

public sealed record LogFileView(
    string RelativePath,
    string DisplayName,
    long SizeBytes,
    DateTimeOffset LastWriteTime);
