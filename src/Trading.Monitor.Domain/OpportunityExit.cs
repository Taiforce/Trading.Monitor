namespace Trading.Monitor.Domain;

public sealed record OpportunityExit(OpportunityStatus Status, DateTimeOffset ExitTime, decimal ExitPrice, string Reason);