namespace GearFlow.Os.Application.Messaging;

public static class SagaTopology
{
    public const string OsEventsExchange = "gearflow.events";
    public const string BrokerOrderCreated = "os.order-created.v1";
    public const string BillingCommandsExchange = "gearflow.commands.v1";
    public const string BillingEventsExchange = "gearflow.events.v1";
    public const string Producer = "gearflow-os-service";

    public const string StartDiagnosticQueue = "operation-start-diagnostic";
    public const string StartExecutionQueue = "operation-start-execution";
    public const string CancelOperationQueue = "operation-cancel-operation";
    public const string ReserveStockQueue = "inventory-reserve-stock";
    public const string ReleaseStockQueue = "inventory-release-stock";
    public const string ConsumeStockQueue = "inventory-consume-stock";

    public const string GenerateBudget = "GenerateBudget";
    public const string RequestPayment = "RequestPayment";
    public const string CancelBudget = "CancelBudget";
    public const string RefundPayment = "RefundPayment";

    public static readonly string[] OperationEventExchanges =
    [
        "operation.diagnostic-finished",
        "operation.diagnostic-failed",
        "operation.execution-finished",
        "operation.canceled",
        "operation.command-rejected"
    ];

    public static readonly string[] InventoryEventExchanges =
    [
        "inventory.stock-reserved",
        "inventory.stock-insufficient",
        "inventory.stock-released",
        "inventory.stock-consumed",
        "inventory.stock-replenished"
    ];

    public static readonly string[] LegacyEventRoutes =
    [
        EventRoutes.OrcamentoGerado,
        EventRoutes.OrcamentoApproved,
        EventRoutes.OrcamentoRejected,
        EventRoutes.OperationCompleted
    ];
}

public static class SagaEventTypes
{
    public const string DiagnosticFinished = "DiagnosticFinished";
    public const string DiagnosticFailed = "DiagnosticFailed";
    public const string BudgetGenerated = "BudgetGenerated";
    public const string BudgetApproved = "BudgetApproved";
    public const string BudgetRejected = "BudgetRejected";
    public const string BudgetGenerationFailed = "BudgetGenerationFailed";
    public const string StockReserved = "StockReserved";
    public const string StockInsufficient = "StockInsufficient";
    public const string StockReleased = "StockReleased";
    public const string StockConsumed = "StockConsumed";
    public const string StockReplenished = "StockReplenished";
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string PaymentFailed = "PaymentFailed";
    public const string PaymentRefunded = "PaymentRefunded";
    public const string ExecutionFinished = "ExecutionFinished";
    public const string OperationCanceled = "OperationCanceled";
}

public sealed record IncomingSagaMessage(
    string Type,
    Guid MessageId,
    Guid ServiceOrderId,
    Guid? BudgetId,
    Guid? PaymentId,
    decimal? Amount,
    string? Currency,
    string RawBody);
