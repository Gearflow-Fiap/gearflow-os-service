namespace GearFlow.Os.Application.Messaging;

public static class EventRoutes
{
    public const string OrderCreated = "order.created";
    public const string OrcamentoGerado = "orcamento.gerado";
    public const string OrcamentoApproved = "orcamento.approved";
    public const string OrcamentoRejected = "orcamento.rejected";
    public const string OperationCompleted = "operation.completed";
}

public sealed record OrderCreatedPayload(
    Guid MessageId,
    Guid ServiceOrderId,
    Guid ClientId,
    Guid VehicleId,
    IReadOnlyList<string> Services,
    DateTime OccurredOn);

public sealed record ServiceOrderEvent(Guid MessageId, Guid ServiceOrderId);
