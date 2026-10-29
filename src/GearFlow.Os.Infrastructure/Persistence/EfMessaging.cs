using System.Text.Json;
using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Os.Infrastructure.Persistence;

public sealed class EfUnitOfWork(OsDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public sealed class EfOutbox(OsDbContext db) : IOutbox
{
    public void Enqueue(string routingKey, object payload) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(payload, OsJson.Options),
            CreatedOn = DateTime.UtcNow
        });
}

public sealed class EfInbox(OsDbContext db) : IInbox
{
    public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken) =>
        db.InboxMessages.AnyAsync(message => message.Id == messageId, cancellationToken);

    public void MarkProcessed(Guid messageId, string type) =>
        db.InboxMessages.Add(new InboxMessage
        {
            Id = messageId,
            Type = type,
            ProcessedOn = DateTime.UtcNow
        });
}
