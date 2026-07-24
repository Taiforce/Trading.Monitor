using System.Globalization;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Services;

public sealed record TradeConversionSummary(
    string Asset,
    decimal Capital,
    decimal EntryPrice,
    decimal? ExitOrMarkPrice,
    decimal Quantity,
    decimal? NetPnL,
    decimal? FinalTotal,
    decimal EstimatedFees,
    decimal BreakEvenPrice,
    decimal BreakEvenMovePercent,
    decimal? GrossPnL,
    string EntryText,
    string ExitText,
    string ResultText,
    string DetailText,
    string CostText,
    string BreakEvenText);

public static class TradeConversionCalculator
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");

    public static TradeConversionSummary Build(
        string symbol,
        MarketSide side,
        decimal capital,
        decimal quantity,
        decimal entryPrice,
        decimal? exitPrice,
        decimal? markPrice,
        decimal? realizedNetPnL,
        decimal estimatedFees)
    {
        var asset = Asset(symbol);
        var priceToUse = exitPrice ?? markPrice;
        decimal? netPnL = null;
        decimal? finalTotal = null;
        decimal? grossPnL = null;
        var entryText = $"Entrada: {Money(capital)} / {Price(entryPrice)} = {Quantity(quantity, asset)}";
        var exitText = "Salida pendiente.";
        var resultText = "Resultado pendiente.";
        var detailText = "Cuando exista precio de salida, se convierte la posicion a dinero final.";
        var breakEvenMove = quantity > 0m ? estimatedFees / quantity : 0m;
        var breakEvenPrice = side == MarketSide.Long ? entryPrice + breakEvenMove : entryPrice - breakEvenMove;
        var breakEvenMovePercent = entryPrice > 0m ? Math.Abs(breakEvenPrice - entryPrice) / entryPrice * 100m : 0m;
        var costText = $"Comision estimada: {Money(estimatedFees)}";
        var breakEvenText = side == MarketSide.Long
            ? $"Necesita subir a {Price(breakEvenPrice)} para empatar"
            : $"Necesita bajar a {Price(breakEvenPrice)} para empatar";

        if (priceToUse.HasValue && entryPrice > 0m && quantity > 0m)
        {
            var gross = OpportunityProjectionService.CalculateGrossPnL(side, entryPrice, priceToUse.Value, quantity);
            grossPnL = Math.Round(gross, 2);
            netPnL = realizedNetPnL ?? Math.Round(gross - estimatedFees, 2);
            finalTotal = Math.Round(capital + netPnL.Value, 2);
            var label = exitPrice.HasValue ? "Salida real" : "Precio actual";
            var gainWord = netPnL.Value >= 0m ? "Ganancia" : "Perdida";
            var sign = netPnL.Value >= 0m ? "+" : "-";

            if (side == MarketSide.Long)
            {
                var exitGrossValue = Math.Round(quantity * priceToUse.Value, 2);
                exitText = $"{label}: {Quantity(quantity, asset)} x {Price(priceToUse.Value)} = {Money(exitGrossValue)}";
                detailText = exitPrice.HasValue
                    ? $"{Money(capital)} compro a {Price(entryPrice)}. Al vender a {Price(priceToUse.Value)}, el total final neto es {Money(finalTotal.Value)}."
                    : $"{Money(capital)} compro a {Price(entryPrice)}. Si vendieras al precio actual {Price(priceToUse.Value)}, el total final neto seria {Money(finalTotal.Value)}.";
            }
            else
            {
                var difference = entryPrice - priceToUse.Value;
                exitText = $"{label}: cierre {Price(priceToUse.Value)}; cambio por {asset} {Price(difference)} x {Quantity(quantity, asset)}";
                detailText = exitPrice.HasValue
                    ? $"{Money(capital)} simulo vender alto desde {Price(entryPrice)}. Al comprar de vuelta a {Price(priceToUse.Value)}, el total final neto es {Money(finalTotal.Value)}."
                    : $"{Money(capital)} simula vender alto desde {Price(entryPrice)}. Si compraras de vuelta al precio actual {Price(priceToUse.Value)}, el total final neto seria {Money(finalTotal.Value)}.";
            }

            resultText = $"{gainWord}: {sign}{Money(Math.Abs(netPnL.Value))} | total final {Money(finalTotal.Value)}";
        }

        return new TradeConversionSummary(asset, capital, entryPrice, priceToUse, quantity, netPnL, finalTotal, estimatedFees, breakEvenPrice, breakEvenMovePercent, grossPnL, entryText, exitText, resultText,
            detailText, costText, $"{breakEvenText} ({breakEvenMovePercent:N2}%).");
    }

    public static string Asset(string symbol)
    {
        if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return symbol[..^4].ToUpperInvariant();

        if (symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
            return symbol[..^3].ToUpperInvariant();

        return symbol.ToUpperInvariant();
    }

    public static string Price(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };
        return value.ToString($"N{decimals}", CurrencyCulture);
    }

    public static string Money(decimal value)
    {
        return value.ToString("C2", CurrencyCulture);
    }

    public static string Quantity(decimal value, string asset)
    {
        return $"{value.ToString("N8", CurrencyCulture)} {asset}";
    }
}
