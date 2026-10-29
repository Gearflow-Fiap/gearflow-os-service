using GearFlow.Os.Domain.Orders;

namespace GearFlow.Os.Application.Dtos;

public sealed record ServiceOrderHistoryDto(string Status, string Message, DateTime CreatedOn);

public sealed record ServiceOrderDto(
    Guid Id,
    int Code,
    Guid ClientId,
    Guid VehicleId,
    string Status,
    bool ExecutionFinished,
    DateTime CreatedOn,
    IReadOnlyList<string> Services,
    IReadOnlyList<ServiceOrderHistoryDto> History)
{
    public static ServiceOrderDto From(ServiceOrder order) => new(
        order.Id,
        order.Code,
        order.ClientId,
        order.VehicleId,
        order.Status.ToString(),
        order.ExecutionFinished,
        order.CreatedOn,
        order.Services.Select(service => service.Description).ToList(),
        order.History.OrderBy(item => item.CreatedOn).ThenBy(item => item.Status).Select(item =>
            new ServiceOrderHistoryDto(item.Status.ToString(), item.Message, item.CreatedOn)).ToList());
}

public sealed record ServiceOrderSummaryDto(Guid Id, int Code, Guid VehicleId, string Status, DateTime CreatedOn)
{
    public static ServiceOrderSummaryDto From(ServiceOrder order) =>
        new(order.Id, order.Code, order.VehicleId, order.Status.ToString(), order.CreatedOn);
}

public sealed record PagedDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record ClientDto(Guid Id, string Name, string Document, string Email, string? Phone, DateTime CreatedOn);

public sealed record VehicleDto(Guid Id, Guid ClientId, string Plate, string Model, DateTime CreatedOn);
