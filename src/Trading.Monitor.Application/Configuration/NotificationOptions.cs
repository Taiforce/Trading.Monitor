namespace Trading.Monitor.Application.Configuration;

public sealed class NotificationOptions
{
    public bool ConsoleEnabled { get; set; } = true;

    public EmailOptions Email { get; set; } = new();

    public TelegramOptions Telegram { get; set; } = new();
}

public sealed class EmailOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string UserName { get; set; } = "";

    /// <summary>Legacy plain-text password. Prefer <see cref="PasswordEnvironmentVariable"/>.</summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// Name of an environment variable holding the SMTP password, mirroring how Binance/OpenAI
    /// secrets are resolved. Takes precedence over <see cref="Password"/> so the secret never
    /// has to live in appsettings/.env files that might be committed or shared.
    /// </summary>
    public string PasswordEnvironmentVariable { get; set; } = "";

    public string From { get; set; } = "";

    public string To { get; set; } = "";

    public string ResolvePassword() =>
        !string.IsNullOrWhiteSpace(PasswordEnvironmentVariable)
            ? Environment.GetEnvironmentVariable(PasswordEnvironmentVariable) ?? ""
            : Password;
}

public sealed class TelegramOptions
{
    public bool Enabled { get; set; }

    /// <summary>Legacy plain-text bot token. Prefer <see cref="BotTokenEnvironmentVariable"/>.</summary>
    public string BotToken { get; set; } = "";

    /// <summary>
    /// Name of an environment variable holding the bot token, mirroring how Binance/OpenAI
    /// secrets are resolved. Takes precedence over <see cref="BotToken"/>.
    /// </summary>
    public string BotTokenEnvironmentVariable { get; set; } = "";

    public string ChatId { get; set; } = "";

    public string ResolveBotToken() =>
        !string.IsNullOrWhiteSpace(BotTokenEnvironmentVariable)
            ? Environment.GetEnvironmentVariable(BotTokenEnvironmentVariable) ?? ""
            : BotToken;
}