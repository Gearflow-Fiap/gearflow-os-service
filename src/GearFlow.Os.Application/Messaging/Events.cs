namespace GearFlow.Os.Application.Messaging;

public static class EventRoutes
{
    /// <summary>Evento informativo de abertura, publicado em gearflow.events. Não dispara nenhuma etapa da Saga.</summary>
    public const string OrderCreatedV1 = "os.order-created.v1";
}

public sealed record OrderCreatedPayload(
    Guid MessageId,
    Guid ServiceOrderId,
    Guid ClientId,
    Guid VehicleId,
    IReadOnlyList<string> Services,
    DateTime OccurredOn);

public sealed record ServiceOrderEvent(Guid MessageId, Guid ServiceOrderId);
