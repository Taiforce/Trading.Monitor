using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Configuration;

namespace Trading.Monitor.Web.Services;

public sealed class ExchangeConnectionStatusService(HttpClient httpClient, IOptionsMonitor<ExchangeExecutionOptions> optionsMonitor)
{
    public async Task<ExchangeConnectionStatus> GetAsync(CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        var apiKeyConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable));
        var apiSecretConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(options.ApiSecretEnvironmentVariable));
        var publicApiHealthy = await IsPublicApiHealthyAsync(cancellationToken);
        var liveTradingAllowed = options.Enabled
                                 && string.Equals(options.Mode, "Live", StringComparison.OrdinalIgnoreCase)
                                 && options.AllowLiveOrders
                                 && apiKeyConfigured
                                 && apiSecretConfigured;

        var safety = liveTradingAllowed
            ? "Live habilitado con limites. Revisa permisos: trading si, retiros no."
            : "Seguro: no ejecuta ordenes reales.";
        var allowedSymbols = (options.AllowedSymbols ?? [])
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var message = !options.Enabled
            ? "Integracion lista, pero desactivada."
            : string.Equals(options.Mode, "Paper", StringComparison.OrdinalIgnoreCase)
                ? "Paper activo: registra simulaciones, no mueve dinero real."
                : !apiKeyConfigured || !apiSecretConfigured
                    ? "Faltan variables BINANCE_API_KEY/BINANCE_API_SECRET para validar cuenta."
                    : liveTradingAllowed
                        ? "Listo para fase live controlada."
                        : "Modo test/live bloqueado por seguridad.";

        return new ExchangeConnectionStatus(
            options.Provider,
            options.Mode,
            publicApiHealthy,
            apiKeyConfigured,
            apiSecretConfigured,
            liveTradingAllowed,
            options.MaxCapitalPerTrade,
            options.DailyLossLimit,
            options.MinimumScoreToExecute,
            options.MinimumExpectedNetProfitPercentAfterCosts,
            options.MaxSlippagePercent,
            options.AllowShortSelling,
            allowedSymbols,
            safety,
            message);
    }

    private async Task<bool> IsPublicApiHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("/api/v3/time", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
