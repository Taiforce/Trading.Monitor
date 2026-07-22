namespace Trading.Monitor.Worker;

public static class LocalEnvFile
{
    public static bool TryLoadNearest(string startDirectory, string fileName)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, fileName);

            if (File.Exists(path))
            {
                Load(path);
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static void Load(string path)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');

            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (!string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
