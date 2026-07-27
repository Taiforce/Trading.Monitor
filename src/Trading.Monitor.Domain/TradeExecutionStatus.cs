namespace Trading.Monitor.Domain;

public enum TradeExecutionStatus
{
    Skipped = 1,
    Blocked = 2,
    Simulated = 3,
    Submitted = 4,
    Filled = 5,
    Failed = 6
}
