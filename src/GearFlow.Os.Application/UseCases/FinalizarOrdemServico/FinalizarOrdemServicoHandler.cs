using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.FinalizarOrdemServico;

public sealed class FinalizarOrdemServicoHandler(IServiceOrderRepository orders, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result> Handle(Guid serviceOrderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(serviceOrderId, cancellationToken);
        if (order is null)
            return Result.Failure(Error.NotFound("Os.NotFound", "Ordem de serviço não encontrada."));

        var delivered = order.Deliver(clock.UtcNow);
        if (delivered.IsFailure)
            return delivered;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
