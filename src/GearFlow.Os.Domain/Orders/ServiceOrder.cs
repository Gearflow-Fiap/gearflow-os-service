using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Domain.Orders;

/// <summary>
/// Ordem de serviço. A máquina segue o fluxo desenhado:
/// Created → WaitingOrcamento → Paid → InProgress → Completed, com Cancelled na rejeição.
/// Paid fica no histórico; o estado persistido depois do pagamento é InProgress.
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

    public Result ApplyOrcamentoGerado(DateTime nowUtc)
    {
        if (Status == ServiceOrderStatus.Cancelled)
            return Conflict("Os.Cancelled", "A OS está cancelada.");
        if (Status != ServiceOrderStatus.Created)
            return Result.Success();
        return Move(ServiceOrderStatus.WaitingOrcamento, "Orçamento gerado. Aguardando aprovação.", nowUtc);
    }

    public Result ApplyOrcamentoApproved(DateTime nowUtc)
    {
        if (Status is ServiceOrderStatus.InProgress or ServiceOrderStatus.Completed)
            return Result.Success();
        if (Status != ServiceOrderStatus.WaitingOrcamento)
            return Conflict("Os.NotWaitingOrcamento", "A OS precisa estar aguardando orçamento para registrar o pagamento.");

        Record(ServiceOrderStatus.Paid, "Orçamento aprovado e pago.", nowUtc);
        return Move(ServiceOrderStatus.InProgress, "OS em execução.", nowUtc);
    }

    public Result ApplyOrcamentoRejected(DateTime nowUtc)
    {
        if (Status == ServiceOrderStatus.Cancelled)
            return Result.Success();
        if (Status != ServiceOrderStatus.WaitingOrcamento)
            return Conflict("Os.CannotCancel", "A OS só pode ser cancelada enquanto aguarda o orçamento.");
        return Move(ServiceOrderStatus.Cancelled, "Orçamento rejeitado. OS cancelada e veículo liberado.", nowUtc);
    }

    public Result ApplyOperationCompleted(DateTime nowUtc)
    {
        if (ExecutionFinished)
            return Result.Success();
        if (Status != ServiceOrderStatus.InProgress)
            return Conflict("Os.NotInProgress", "A operação só pode ser concluída com a OS em execução.");

        ExecutionFinished = true;
        UpdatedOn = nowUtc;
        _history.Add(new ServiceOrderHistory(Guid.NewGuid(), Status, "Operação concluída. Aguardando entrega.", nowUtc));
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
