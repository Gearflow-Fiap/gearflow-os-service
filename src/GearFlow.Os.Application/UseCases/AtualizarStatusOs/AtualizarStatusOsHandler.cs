using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Domain.Orders;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.AtualizarStatusOs;

public sealed class AtualizarStatusOsHandler(
    IServiceOrderRepository orders,
    IInbox inbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public Task<Result> QuandoOrcamentoGerado(Guid messageId, Guid serviceOrderId, CancellationToken cancellationToken) =>
        Apply(messageId, EventRoutes.OrcamentoGerado, serviceOrderId, static (order, now) => order.ApplyOrcamentoGerado(now), cancellationToken);

    public Task<Result> QuandoOrcamentoAprovado(Guid messageId, Guid serviceOrderId, CancellationToken cancellationToken) =>
        Apply(messageId, EventRoutes.OrcamentoApproved, serviceOrderId, static (order, now) => order.ApplyOrcamentoApproved(now), cancellationToken);

    public Task<Result> QuandoOrcamentoRejeitado(Guid messageId, Guid serviceOrderId, CancellationToken cancellationToken) =>
        Apply(messageId, EventRoutes.OrcamentoRejected, serviceOrderId, static (order, now) => order.ApplyOrcamentoRejected(now), cancellationToken);

    public Task<Result> QuandoOperacaoConcluida(Guid messageId, Guid serviceOrderId, CancellationToken cancellationToken) =>
        Apply(messageId, EventRoutes.OperationCompleted, serviceOrderId, static (order, now) => order.ApplyOperationCompleted(now), cancellationToken);

    private async Task<Result> Apply(
        Guid messageId,
        string type,
        Guid serviceOrderId,
        Func<ServiceOrder, DateTime, Result> transition,
        CancellationToken cancellationToken)
    {
        if (messageId != Guid.Empty && await inbox.ExistsAsync(messageId, cancellationToken))
            return Result.Success();

        var order = await orders.GetByIdAsync(serviceOrderId, cancellationToken);
        if (order is null)
            return Result.Failure(Error.NotFound("Os.NotFound", "Ordem de serviço não encontrada."));

        var result = transition(order, clock.UtcNow);
        if (result.IsFailure)
            return result;

        if (messageId != Guid.Empty)
            inbox.MarkProcessed(messageId, type);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
