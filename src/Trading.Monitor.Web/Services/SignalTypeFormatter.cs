using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Services;

public static class SignalTypeFormatter
{
    public const string BuyLowSellHigh = SignalTypeDescriptor.BuyLowSellHigh;
    public const string SellHighBuyLow = SignalTypeDescriptor.SellHighBuyLow;

    public static string Value(MarketSide side)
    {
        return SignalTypeDescriptor.Value(side);
    }

    public static bool Matches(MarketSide side, string? filter)
    {
        return SignalTypeDescriptor.Matches(side, filter);
    }

    public static int Priority(MarketSide side)
    {
        return SignalTypeDescriptor.Priority(side);
    }

    public static string Label(MarketSide side)
    {
        return SignalTypeDescriptor.Label(side);
    }

    public static string Description(MarketSide side)
    {
        return SignalTypeDescriptor.Description(side);
    }

    public static string Requirement(MarketSide side)
    {
        return SignalTypeDescriptor.Requirement(side);
    }

    public static string EntryVerb(MarketSide side)
    {
        return SignalTypeDescriptor.EntryVerb(side);
    }

    public static string ExitVerb(MarketSide side)
    {
        return SignalTypeDescriptor.ExitVerb(side);
    }

    public static string Normalize(string? value)
    {
        return SignalTypeDescriptor.Normalize(value);
    }
}
