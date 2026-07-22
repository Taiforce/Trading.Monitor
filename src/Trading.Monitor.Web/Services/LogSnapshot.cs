namespace Trading.Monitor.Web.Services;

public sealed record LogSnapshot(
    LogFileView? File,
    IReadOnlyList<string> Lines,
    string RootPath,
    string? ErrorMessage);
