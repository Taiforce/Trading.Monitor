namespace Trading.Monitor.Web.Configuration;

public sealed class AdminAccessOptions
{
    public const string SectionName = "AdminAccess";

    /// <summary>
    /// Well-known/example passwords that must never be accepted, even if an operator
    /// copies a sample value from documentation into a real deployment.
    /// </summary>
    public static readonly string[] DisallowedPasswords =
    [
        "local-development-only", "admin", "password", "changeme", "123456", "trading-monitor",
        "trademonitor", "tradingmonitor", "admin123", "letmein", "qwerty"
    ];

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Explicit, auditable opt-out for authentication. Must be set to <c>true</c> deliberately
    /// (e.g. only for local development) for the site to serve anonymous traffic when
    /// <see cref="Enabled"/> is <c>false</c>. Defaulting to <c>false</c> means a misconfigured
    /// or partially-copied environment fails closed instead of silently exposing every page/API.
    /// </summary>
    public bool AllowAnonymousAccess { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int SessionHours { get; set; } = 8;

    public int MinimumPasswordLength { get; set; } = 12;
}
