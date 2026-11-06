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
}

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public DateTime ProcessedOn { get; set; }
}
