using System.Net;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Domain;
using Trading.Monitor.Infrastructure.Ai;

namespace Trading.Monitor.Tests;

public sealed class OpenAiResearchAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_BacksOffAfterApiFailure()
    {
        var environmentVariable = $"OPENAI_API_KEY_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentVariable, "test-key");

        try
        {
            var handler = new StubHandler();
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
            var telemetry = new RecordingTelemetry();
            var options = new OpenAiOptions
            {
                ApiKeyEnvironmentVariable = environmentVariable,
                MinimumNewsItemsToAnalyze = 0,
                MinimumMinutesBetweenCalls = 15
            };
            var analyzer = new OpenAiResearchAnalyzer(client, options, telemetry);

            var first = await analyzer.AnalyzeAsync(["BTCUSDT"], [], CancellationToken.None);
            var second = await analyzer.AnalyzeAsync(["BTCUSDT"], [], CancellationToken.None);

            Assert.Empty(first);
            Assert.Empty(second);
            Assert.Equal(1, handler.RequestCount);
            Assert.Contains(telemetry.Events, item => item.Status == DataSourceStatus.Failed);
            Assert.Contains(telemetry.Events, item => item.Message.Contains("waiting", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"quota unavailable\"}}")
            });
        }
    }

    private sealed class RecordingTelemetry : ISourceTelemetryRecorder
    {
        public List<DataSourceHealthEvent> Events { get; } = [];

        public Task RecordAsync(DataSourceHealthEvent healthEvent, CancellationToken cancellationToken)
        {
            Events.Add(healthEvent);
            return Task.CompletedTask;
        }

        public Task SaveResearchItemsAsync(IReadOnlyList<NewsItem> items, DataSourceKind kind, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
