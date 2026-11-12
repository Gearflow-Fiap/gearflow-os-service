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
        Enqueue(new OutgoingMessage
        {
            RoutingKey = routingKey,
            Payload = payload,
            MessageId = TryReadGuid(payload, "MessageId") ?? Guid.NewGuid(),
            CorrelationId = TryReadGuid(payload, "ServiceOrderId") ?? Guid.Empty
        });

    public void Enqueue(OutgoingMessage message)
    {
        // O dispatcher publica em segundo plano, quando a requisição (ou a mensagem) que gerou isto já terminou:
        // guarda-se o contexto agora para o trace continuar no próximo serviço.
        var trace = OsTelemetry.CaptureCurrent();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Exchange = message.Exchange,
            Queue = message.Queue,
            RoutingKey = message.RoutingKey,
            Payload = JsonSerializer.Serialize(message.Payload, OsJson.Options),
            ContentType = message.ContentType,
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            CreatedOn = DateTime.UtcNow,
            TraceParent = trace.TraceParent,
            TraceState = trace.TraceState
        });
    }

    private static Guid? TryReadGuid(object payload, string property)
    {
        var value = payload.GetType().GetProperty(property)?.GetValue(payload);
        return value is Guid guid ? guid : null;
    }
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
