using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Domain.Orders;

/// <summary>
/// Ordem de serviço. O OS é o orquestrador da Saga:
/// Created → WaitingOrcamento → AwaitingStock → AwaitingPayment → InProgress → Completed.
/// Paid fica no histórico no momento da confirmação do pagamento.
/// Sem peças, a aprovação pula AwaitingStock e vai para AwaitingPayment.
/// </summary>
public sealed class ServiceOrder : Entity
{
    private readonly List<RequestedService> _services = [];
    private readonly List<ServiceOrderHistory> _history = [];

    public int Code { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid VehicleId { get; private set; }
    public ServiceOrderStatus Status { get; private set; }
    public bool ExecutionFinished { get; private set; }
    public bool StockReserved { get; private set; }
    public bool StockConsumed { get; private set; }
    public Guid? BudgetId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string? DiagnosticSnapshotJson { get; private set; }
    public DateTime CreatedOn { get; private set; }
    public DateTime? UpdatedOn { get; private set; }

    public IReadOnlyCollection<RequestedService> Services => _services;
    public IReadOnlyCollection<ServiceOrderHistory> History => _history;

    private ServiceOrder()
    {
    }

    public static Result<ServiceOrder> Open(Guid clientId, Guid vehicleId, IReadOnlyList<string> services, DateTime nowUtc)
    {
        if (clientId == Guid.Empty)
            return Result.Failure<ServiceOrder>(Error.Validation("Os.ClientRequired", "Informe o cliente."));
        if (vehicleId == Guid.Empty)
            return Result.Failure<ServiceOrder>(Error.Validation("Os.VehicleRequired", "Informe o veículo."));

        var cleaned = (services ?? [])
            .Select(service => service?.Trim() ?? "")
            .Where(service => service.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (cleaned.Count == 0)
            return Result.Failure<ServiceOrder>(Error.Validation("Os.NoServices", "Informe ao menos um serviço."));

        var order = new ServiceOrder
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            VehicleId = vehicleId,
            Status = ServiceOrderStatus.Created,
            CreatedOn = nowUtc
        };
        foreach (var service in cleaned)
            order._services.Add(new RequestedService(Guid.NewGuid(), service));
        order._history.Add(new ServiceOrderHistory(Guid.NewGuid(), ServiceOrderStatus.Created, "Ordem de serviço aberta.", nowUtc));
        return Result.Success(order);
    }

    internal void AssignCode(int code) => Code = code;

    public void CaptureDiagnostic(string snapshotJson, DateTime nowUtc)
    {
        DiagnosticSnapshotJson = snapshotJson;
        UpdatedOn = nowUtc;
    }

    public void CaptureBudget(Guid budgetId, DateTime nowUtc)
    {
        if (budgetId != Guid.Empty)
            BudgetId = budgetId;
        UpdatedOn = nowUtc;
    }

    public Result ApplyOrcamentoGerado(DateTime nowUtc)
    {
        if (Status == ServiceOrderStatus.Cancelled)
            return Conflict("Os.Cancelled", "A OS está cancelada.");
        if (Status != ServiceOrderStatus.Created)
            return Result.Success();
        return Move(ServiceOrderStatus.WaitingOrcamento, "Orçamento gerado. Aguardando aprovação.", nowUtc);
    }

    public Result ApplyOrcamentoApproved(DateTime nowUtc, bool hasStockItems = true)
    {
        if (Status is ServiceOrderStatus.InProgress or ServiceOrderStatus.Completed)
            return Result.Success();
        if (Status is ServiceOrderStatus.AwaitingStock or ServiceOrderStatus.AwaitingPayment or ServiceOrderStatus.AwaitingParts)
            return Result.Success();
        if (Status != ServiceOrderStatus.WaitingOrcamento)
            return Conflict("Os.NotWaitingOrcamento", "A OS precisa estar aguardando orçamento para registrar a aprovação.");

        return hasStockItems
            ? Move(ServiceOrderStatus.AwaitingStock, "Orçamento aprovado. Reservando estoque.", nowUtc)
            : Move(ServiceOrderStatus.AwaitingPayment, "Orçamento aprovado. Sem peças. Aguardando pagamento.", nowUtc);
    }

    public Result ApplyOrcamentoRejected(DateTime nowUtc)
    {
        if (Status == ServiceOrderStatus.Cancelled)
            return Result.Success();
        if (Status is ServiceOrderStatus.InProgress or ServiceOrderStatus.Completed)
            return Conflict("Os.CannotCancel", "A OS já está em execução e não cancela por rejeição de orçamento.");
        if (Status is not (ServiceOrderStatus.WaitingOrcamento or ServiceOrderStatus.AwaitingStock
            or ServiceOrderStatus.AwaitingPayment or ServiceOrderStatus.AwaitingParts or ServiceOrderStatus.Created))
            return Conflict("Os.CannotCancel", "A OS só pode ser cancelada antes da execução.");
        return Move(ServiceOrderStatus.Cancelled, "Orçamento rejeitado. OS cancelada e veículo liberado.", nowUtc);
    }

    public Result ApplyStockReserved(DateTime nowUtc)
    {
        if (Status is ServiceOrderStatus.AwaitingPayment or ServiceOrderStatus.InProgress or ServiceOrderStatus.Completed)
            return Result.Success();
        if (Status is not (ServiceOrderStatus.AwaitingStock or ServiceOrderStatus.AwaitingParts))
            return Conflict("Os.NotAwaitingStock", "A reserva de estoque não era esperada neste estado.");

        StockReserved = true;
        return Move(ServiceOrderStatus.AwaitingPayment, "Estoque reservado. Aguardando pagamento.", nowUtc);
    }

    public Result ApplyStockInsufficient(DateTime nowUtc)
    {
        if (Status == ServiceOrderStatus.AwaitingParts)
            return Result.Success();
        if (Status != ServiceOrderStatus.AwaitingStock)
            return Conflict("Os.NotAwaitingStock", "Falta de estoque só se aplica durante a reserva.");
        return Move(ServiceOrderStatus.AwaitingParts, "Estoque insuficiente. Aguardando reposição.", nowUtc);
    }

    public Result RetryStockAfterReplenish(DateTime nowUtc)
    {
        if (Status != ServiceOrderStatus.AwaitingParts)
            return Result.Success();
        return Move(ServiceOrderStatus.AwaitingStock, "Estoque reposto. Nova reserva solicitada.", nowUtc);
    }

    public Result ApplyPaymentConfirmed(Guid paymentId, DateTime nowUtc)
    {
        if (Status is ServiceOrderStatus.InProgress or ServiceOrderStatus.Completed)
            return Result.Success();
        if (Status != ServiceOrderStatus.AwaitingPayment)
            return Conflict("Os.NotAwaitingPayment", "A OS precisa estar aguardando pagamento.");

        if (paymentId != Guid.Empty)
            PaymentId = paymentId;
        Record(ServiceOrderStatus.Paid, "Pagamento confirmado.", nowUtc);
        return Move(ServiceOrderStatus.InProgress, "OS em execução.", nowUtc);
    }

    public Result ApplyPaymentFailed(DateTime nowUtc)
    {
        if (Status is ServiceOrderStatus.InProgress or ServiceOrderStatus.Completed or ServiceOrderStatus.Cancelled)
            return Result.Success();
        if (Status != ServiceOrderStatus.AwaitingPayment)
            return Conflict("Os.NotAwaitingPayment", "Falha de pagamento fora do estado esperado.");
        UpdatedOn = nowUtc;
        _history.Add(new ServiceOrderHistory(Guid.NewGuid(), Status, "Pagamento falhou. Compensando reserva de estoque.", nowUtc));
        return Result.Success();
    }

    public Result ApplyOperationCompleted(DateTime nowUtc)
    {
        if (ExecutionFinished)
            return Result.Success();
        if (Status != ServiceOrderStatus.InProgress)
            return Conflict("Os.NotInProgress", "A operação só pode ser concluída com a OS em execução.");

        ExecutionFinished = true;
        UpdatedOn = nowUtc;
        _history.Add(new ServiceOrderHistory(Guid.NewGuid(), Status, "Operação concluída. Aguardando baixa de estoque e entrega.", nowUtc));
        return Result.Success();
    }

    public Result ApplyStockConsumed(DateTime nowUtc)
    {
        if (StockConsumed)
            return Result.Success();
        StockConsumed = true;
        UpdatedOn = nowUtc;
        _history.Add(new ServiceOrderHistory(Guid.NewGuid(), Status, "Estoque baixado.", nowUtc));
        return Result.Success();
    }

    public Result Deliver(DateTime nowUtc)
    {
        if (Status == ServiceOrderStatus.Completed)
            return Result.Success();
        if (Status != ServiceOrderStatus.InProgress)
            return Conflict("Os.NotInProgress", "A OS precisa estar em execução para ser entregue.");
        if (!ExecutionFinished)
            return Conflict("Os.ExecutionPending", "Aguardando a finalização da operação.");
        if (StockReserved && !StockConsumed)
            return Conflict("Os.StockPending", "Aguardando a baixa definitiva do estoque.");
        return Move(ServiceOrderStatus.Completed, "Veículo entregue. OS concluída.", nowUtc);
    }

    private Result Move(ServiceOrderStatus status, string message, DateTime nowUtc)
    {
        Record(status, message, nowUtc);
        return Result.Success();
    }

    private void Record(ServiceOrderStatus status, string message, DateTime nowUtc)
    {
        Status = status;
        UpdatedOn = nowUtc;
        _history.Add(new ServiceOrderHistory(Guid.NewGuid(), status, message, nowUtc));
    }

    private static Result Conflict(string code, string message) => Result.Failure(Error.Conflict(code, message));
}
