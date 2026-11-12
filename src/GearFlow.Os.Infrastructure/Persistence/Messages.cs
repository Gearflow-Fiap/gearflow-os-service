namespace GearFlow.Os.Infrastructure.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string? Exchange { get; set; }
    public string? Queue { get; set; }
    public string RoutingKey { get; set; } = "";
    public string Payload { get; set; } = "";
    public string ContentType { get; set; } = "application/json";
    public Guid MessageId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? ProcessedOn { get; set; }

    /// <summary>Contexto de trace (W3C) de quem enfileirou a mensagem; o dispatcher o envia como header para o trace continuar nos vizinhos.</summary>
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
}

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public DateTime ProcessedOn { get; set; }
}
