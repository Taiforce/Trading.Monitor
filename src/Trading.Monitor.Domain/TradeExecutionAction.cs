namespace Trading.Monitor.Domain;

public enum TradeExecutionAction
{
    BuyToOpen = 1,
    SellToClose = 2,
    SellToOpen = 3,
    BuyToClose = 4
}
