using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Saga;
using GearFlow.Os.Domain.Orders;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.CancelarOrdemServico;

/// <summary>
/// Cancelamento pela recepção, por exemplo quando a OS está parada aguardando peças ou orçamento.
/// Dispara as mesmas compensações da Saga: cancela a operação, libera estoque e estorna ou cancela o orçamento.
/// </summary>
public sealed class CancelarOrdemServicoHandler(
    IServiceOrderRepository orders,
    SagaOrchestrator saga,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> Handle(Guid serviceOrderId, string? motivo, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(serviceOrderId, cancellationToken);
        if (order is null)
            return Result.Failure(Error.NotFound("Os.NotFound", "Ordem de serviço não encontrada."));
        if (order.Status == ServiceOrderStatus.Cancelled)
            return Result.Success();

        var reason = string.IsNullOrWhiteSpace(motivo) ? "solicitado pela recepção" : motivo.Trim();
        var cancelled = order.CancelBySagaFailure(reason, clock.UtcNow);
        if (cancelled.IsFailure)
            return cancelled;

        saga.Compensate(order, "cancelled-by-staff");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
