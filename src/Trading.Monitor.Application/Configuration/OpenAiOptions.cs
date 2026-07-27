namespace Trading.Monitor.Application.Configuration;

public sealed class OpenAiOptions
{
    public bool Enabled { get; set; } = true;

    public string ApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";

    public string BaseUrl { get; set; } = "https://api.openai.com";

    public string Model { get; set; } = "gpt-4.1-mini";

    public int TimeoutSeconds { get; set; } = 20;

    public int MaxNewsItems { get; set; } = 12;

    public int MinimumNewsItemsToAnalyze { get; set; } = 3;

    public int MinimumMinutesBetweenCalls { get; set; } = 15;

    public int MaxPromptCharacters { get; set; } = 6000;

    public bool OnlyAnalyzeWhenNewsChanged { get; set; } = true;
}
