namespace GearFlow.Os.Application.Messaging;

public sealed class OutgoingMessage
{
    public string? Exchange { get; init; }
    public string? Queue { get; init; }
    public required string RoutingKey { get; init; }
    public required object Payload { get; init; }
    public string ContentType { get; init; } = "application/json";
    public Guid MessageId { get; init; }
    public Guid CorrelationId { get; init; }
}
