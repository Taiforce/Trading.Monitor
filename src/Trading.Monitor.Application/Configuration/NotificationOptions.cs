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

    public string Password { get; set; } = "";

    public string From { get; set; } = "";

    public string To { get; set; } = "";
}

public sealed class TelegramOptions
{
    public bool Enabled { get; set; }

    public string BotToken { get; set; } = "";

    public string ChatId { get; set; } = "";
}