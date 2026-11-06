using System.Text.Json;

namespace GearFlow.Os.Application.Messaging;

public static class InventoryItemTypes
{
    public const string Part = "Part";
    public const string Consumable = "Consumable";
}

public sealed record StockItemLine(string ItemType, Guid ItemId, decimal Quantity);

public sealed record ReserveStock(Guid CorrelationId, Guid MessageId, DateTime OccurredOn, IReadOnlyList<StockItemLine> Items);

public sealed record ReleaseStock(Guid CorrelationId, Guid MessageId, DateTime OccurredOn, string Reason);

public sealed record ConsumeStock(Guid CorrelationId, Guid MessageId, DateTime OccurredOn);

public sealed record RequestedJob(Guid LineId, Guid CatalogJobId);

public sealed record RequestedPart(Guid LineId, Guid PartId, int Quantity);

public sealed record CommercialLine(
    Guid LineId,
    string ItemType,
    Guid ItemId,
    string Name,
    decimal Quantity,
    int UnitPriceCents,
    int LineTotalCents);

public sealed record DiagnosticSnapshot(
    Guid DiagnosticId,
    string Currency,
    string PricingPolicyId,
    DateTimeOffset PricedAt,
    int TotalPriceCents,
    CommercialLine[] Items);

public sealed record ApprovedJob(Guid BudgetJobId, Guid DiagnosticLineId);

public sealed record StartDiagnostic(
    Guid CorrelationId,
    Guid MessageId,
    DateTimeOffset OccurredOn,
    Guid VehicleId,
    int? ServiceOrderCode,
    RequestedJob[] RequestedJobs,
    RequestedPart[] RequestedParts);

public sealed record StartExecution(
    Guid CorrelationId,
    Guid MessageId,
    DateTimeOffset OccurredOn,
    Guid BudgetId,
    int BudgetRevision,
    DiagnosticSnapshot Snapshot,
    ApprovedJob[] Jobs);

public sealed record CancelOperation(Guid CorrelationId, Guid MessageId, DateTimeOffset OccurredOn, string Reason);

public sealed record BillingCustomer(string Name, string Email);

public sealed record BillingItem(Guid ReferenceId, string Type, string Description, decimal UnitPrice, decimal Quantity);

public sealed record BillingEnvelope(
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    DateTimeOffset OccurredOn,
    Guid CorrelationId,
    Guid ServiceOrderId,
    Guid? CausationId,
    string Producer,
    JsonElement Payload);

public sealed class MassTransitEnvelope
{
    public Guid MessageId { get; init; }
    public Guid ConversationId { get; init; }
    public Guid CorrelationId { get; init; }
    public string SourceAddress { get; init; } = "";
    public string DestinationAddress { get; init; } = "";
    public string[] MessageType { get; init; } = [];
    public object Message { get; init; } = new();
}
