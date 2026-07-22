using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class DataSourceEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public DataSourceKind Kind { get; set; }

    public DataSourceStatus Status { get; set; }

    public string? Url { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastFailureAt { get; set; }

    public int FailureCount { get; set; }

    public string LastMessage { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
