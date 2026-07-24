using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public static class SignalTypeDescriptor
{
    public const string BuyLowSellHigh = "compra-bajo-vende-alto";
    public const string SellHighBuyLow = "vende-alto-compra-bajo";

    public static string Value(MarketSide side)
    {
        return side == MarketSide.Long ? BuyLowSellHigh : SellHighBuyLow;
    }

    public static bool Matches(MarketSide side, string? filter)
    {
        var normalized = Normalize(filter);
        return string.IsNullOrWhiteSpace(normalized) || string.Equals(Value(side), normalized, StringComparison.Ordinal);
    }

    public static int Priority(MarketSide side)
    {
        return side == MarketSide.Long ? 0 : 1;
    }

    public static string Label(MarketSide side)
    {
        return side == MarketSide.Long ? "Compra bajo - vende alto" : "Vende alto - compra bajo";
    }

    public static string Description(MarketSide side)
    {
        return side == MarketSide.Long
            ? "Compras BTC/ETH con tu dinero y buscas venderlo mas caro."
            : "Vendes primero y buscas recomprar mas barato; requiere tener moneda, margen o futuros.";
    }

    public static string Requirement(MarketSide side)
    {
        return side == MarketSide.Long ? "Apta para spot si tienes dinero disponible." : "No es spot simple si no tienes BTC/ETH.";
    }

    public static string EntryVerb(MarketSide side)
    {
        return side == MarketSide.Long ? "comprar bajo" : "vender alto";
    }

    public static string ExitVerb(MarketSide side)
    {
        return side == MarketSide.Long ? "vender alto" : "comprar bajo";
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.Trim().ToLowerInvariant() switch
        {
            "long" or "compra" or "comprar" or "spot" or BuyLowSellHigh => BuyLowSellHigh,
            "short" or "venta" or "vender" or SellHighBuyLow => SellHighBuyLow,
            "todas" or "todos" or "all" => "",
            _ => ""
        };
    }
}
