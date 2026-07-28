using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Services;

public static class SignalSegmentation
{
    public static bool MatchesOperationMode(OpportunityReportRow row, string? mode)
    {
        return NormalizeOperationMode(mode) switch
        {
            "classic" or "fija" or "fixed" => row.OperationKind == SignalOperationKind.Fixed,
            "managed" or "seguimiento" => row.OperationKind == SignalOperationKind.Managed,
            "traders" or "trader" => row.OperationKind == SignalOperationKind.Trader,
            _ => true
        };
    }

    public static bool MatchesOriginView(OpportunityReportRow row, string? view)
    {
        return NormalizeOriginView(view) switch
        {
            "ia" => row.OriginKind == SignalOriginKind.ExternalAi,
            "traders" => row.OriginKind == SignalOriginKind.Trader,
            _ => row.OriginKind == SignalOriginKind.OwnAi
        };
    }

    public static string OperationKindLabel(SignalOperationKind kind)
    {
        return kind switch
        {
            SignalOperationKind.Managed => "Seguimiento",
            SignalOperationKind.Trader => "Trader",
            _ => "Señal fija"
        };
    }

    public static string OperationKindHint(SignalOperationKind kind)
    {
        return kind switch
        {
            SignalOperationKind.Managed => "Entrada sin salida fija; el sistema busca el mejor cierre después.",
            SignalOperationKind.Trader => "Operación capturada desde un trader externo.",
            _ => "Entrada con objetivo y fecha límite definidos desde el inicio."
        };
    }

    public static string OriginKindLabel(SignalOriginKind kind)
    {
        return kind switch
        {
            SignalOriginKind.ExternalAi => "IA ajena",
            SignalOriginKind.Trader => "Trader",
            _ => "IA propia"
        };
    }

    public static string NormalizeOriginView(string? view)
    {
        return view?.Trim().ToLowerInvariant() switch
        {
            "ia" or "externa" or "ajena" or "external" => "ia",
            "trader" or "traders" => "traders",
            _ => "propio"
        };
    }

    private static string NormalizeOperationMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() ?? "";
    }
}
