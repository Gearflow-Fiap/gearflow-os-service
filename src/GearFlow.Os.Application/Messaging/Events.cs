namespace GearFlow.Os.Application.Messaging;

public static class EventRoutes
{
    public const string OrderCreated = "os.created";
    public const string OrcamentoGerado = "budget.generated";
    public const string OrcamentoApproved = "budget.approved";
    public const string OrcamentoRejected = "budget.rejected";
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
