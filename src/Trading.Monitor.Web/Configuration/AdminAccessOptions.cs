namespace Trading.Monitor.Web.Configuration;

public sealed class AdminAccessOptions
{
    public const string SectionName = "AdminAccess";

    public bool Enabled { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int SessionHours { get; set; } = 8;
}
