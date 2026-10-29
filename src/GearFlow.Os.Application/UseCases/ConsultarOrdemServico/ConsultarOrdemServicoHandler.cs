using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Dtos;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.ConsultarOrdemServico;

public sealed class ConsultarOrdemServicoHandler(IServiceOrderRepository orders)
{
    public async Task<Result<ServiceOrderDto>> PorId(Guid id, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(id, cancellationToken);
        if (order is null)
            return Result.Failure<ServiceOrderDto>(Error.NotFound("Os.NotFound", "Ordem de serviço não encontrada."));
        return Result.Success(ServiceOrderDto.From(order));
    }

    public async Task<PagedDto<ServiceOrderSummaryDto>> Listar(int page, int pageSize, bool incluirEncerradas, CancellationToken cancellationToken)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;
        var result = await orders.ListAsync(new ServiceOrderListFilter(page, pageSize, incluirEncerradas), cancellationToken);
        return new PagedDto<ServiceOrderSummaryDto>(
            result.Items.Select(ServiceOrderSummaryDto.From).ToList(),
            result.Page,
            result.PageSize,
            result.Total);
    }
}
