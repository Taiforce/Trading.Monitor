namespace Trading.Monitor.Domain;

public enum OpportunityStatus { Open = 0, HitStopLoss = 1, HitTakeProfit1 = 2, HitTakeProfit2 = 3, Expired = 4, ManuallyClosed = 5 }