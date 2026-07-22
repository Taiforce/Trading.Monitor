namespace Trading.Monitor.Web.Services;

public sealed class OperationalLogReader(IConfiguration configuration, IWebHostEnvironment environment, ILogger<OperationalLogReader> logger)
{
    public IReadOnlyList<LogFileView> ListFiles()
    {
        var root = ResolveRootPath();

        if (!Directory.Exists(root))
            return [];

        try
        {
            return Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    return new LogFileView(relative, relative, info.Length, info.LastWriteTimeUtc);
                })
                .OrderByDescending(file => file.LastWriteTime)
                .Take(50)
                .ToArray();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not list log files from {RootPath}.", root);
            return [];
        }
    }

    public LogSnapshot Read(string? requestedFile, int lineLimit)
    {
        var root = ResolveRootPath();
        var files = ListFiles();
        var selected = ResolveSelectedFile(files, requestedFile);

        if (selected is null)
            return new LogSnapshot(null, [], root, Directory.Exists(root) ? null : "La carpeta de logs no existe todavia.");

        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(fullRoot, selected.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return new LogSnapshot(null, [], root, "Archivo fuera de la carpeta permitida.");

        try
        {
            return new LogSnapshot(selected, ReadTail(path, Math.Clamp(lineLimit, 50, 1000)), root, null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read log file {LogFile}.", selected.RelativePath);
            return new LogSnapshot(selected, [], root, exception.Message);
        }
    }

    private static LogFileView? ResolveSelectedFile(IReadOnlyList<LogFileView> files, string? requestedFile)
    {
        if (!string.IsNullOrWhiteSpace(requestedFile))
        {
            var selected = files.FirstOrDefault(file => string.Equals(file.RelativePath, requestedFile, StringComparison.OrdinalIgnoreCase));

            if (selected is not null)
                return selected;
        }

        return files.FirstOrDefault();
    }

    private IReadOnlyList<string> ReadTail(string path, int lineLimit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new Queue<string>();

        while (reader.ReadLine() is { } line)
        {
            lines.Enqueue(line);

            if (lines.Count > lineLimit)
                lines.Dequeue();
        }

        return lines.ToArray();
    }

    private string ResolveRootPath()
    {
        var configured = configuration["Logs:DirectoryPath"] ?? "../../logs";
        return Path.IsPathFullyQualified(configured) ? configured : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
    }
}
