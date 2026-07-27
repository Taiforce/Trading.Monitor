using Microsoft.EntityFrameworkCore;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Persistence;

public sealed class EfTradeExecutionRepository(TradingMonitorDbContext dbContext) : ITradeExecutionRepository
{
    public async Task SaveAsync(TradeExecutionAudit audit, CancellationToken cancellationToken)
    {
        dbContext.TradeExecutions.Add(new TradeExecutionEntity
        {
            Id = audit.Id == Guid.Empty ? Guid.NewGuid() : audit.Id,
            OpportunityId = audit.OpportunityId,
            Symbol = audit.Symbol,
            Side = audit.Side,
            Action = audit.Action,
            Mode = audit.Mode,
            Status = audit.Status,
            RequestedCapital = audit.RequestedCapital,
            RequestedQuantity = audit.RequestedQuantity,
            ExecutedQuantity = audit.ExecutedQuantity,
            ExecutedQuote = audit.ExecutedQuote,
            Price = audit.Price,
            ClientOrderId = audit.ClientOrderId,
            ExchangeOrderId = audit.ExchangeOrderId,
            Reason = audit.Reason,
            Message = audit.Message,
            RequestJson = audit.RequestJson,
            ResponseJson = audit.ResponseJson,
            CreatedAt = audit.CreatedAt == default ? DateTimeOffset.UtcNow : audit.CreatedAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TradeExecutionAudit?> GetLatestEntryAsync(Guid opportunityId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.TradeExecutions.AsNoTracking()
            .Where(row => row.OpportunityId == opportunityId && (row.Action == TradeExecutionAction.BuyToOpen || row.Action == TradeExecutionAction.SellToOpen))
            .OrderByDescending(row => row.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : ToAudit(entity);
    }

    public async Task<IReadOnlyList<TradeExecutionAudit>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        return await GetRecentByStatusAsync(null, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<TradeExecutionAudit>> GetRecentByStatusAsync(TradeExecutionStatus? status, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.TradeExecutions.AsNoTracking();

        if (status.HasValue)
            query = query.Where(row => row.Status == status.Value);

        var entities = await query.OrderByDescending(row => row.CreatedAt)
            .Take(Math.Clamp(limit, 1, 300))
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToAudit).ToArray();
    }

    private static TradeExecutionAudit ToAudit(TradeExecutionEntity entity)
    {
        return new TradeExecutionAudit(
            entity.Id,
            entity.OpportunityId,
            entity.Symbol,
            entity.Side,
            entity.Action,
            entity.Mode,
            entity.Status,
            entity.RequestedCapital,
            entity.RequestedQuantity,
            entity.ExecutedQuantity,
            entity.ExecutedQuote,
            entity.Price,
            entity.ClientOrderId,
            entity.ExchangeOrderId,
            entity.Reason,
            entity.Message,
            entity.RequestJson,
            entity.ResponseJson,
            entity.CreatedAt);
    }
}
