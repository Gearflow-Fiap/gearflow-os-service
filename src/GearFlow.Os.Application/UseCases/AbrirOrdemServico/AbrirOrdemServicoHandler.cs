using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Dtos;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Domain.Orders;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.AbrirOrdemServico;

public sealed record AbrirOrdemServicoCommand(Guid VehicleId, IReadOnlyList<string> Services);

public sealed class AbrirOrdemServicoHandler(
    IVehicleRepository vehicles,
    IServiceOrderRepository orders,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<ServiceOrderDto>> Handle(AbrirOrdemServicoCommand command, CancellationToken cancellationToken)
    {
        var vehicle = await vehicles.GetByIdAsync(command.VehicleId, cancellationToken);
        if (vehicle is null)
            return Result.Failure<ServiceOrderDto>(Error.NotFound("Vehicle.NotFound", "Veículo não encontrado."));

        var opened = ServiceOrder.Open(vehicle.ClientId, vehicle.Id, command.Services, clock.UtcNow);
        if (opened.IsFailure)
            return Result.Failure<ServiceOrderDto>(opened.Error);

        var order = opened.Value;
        await orders.AddAsync(order, cancellationToken);
        outbox.Enqueue(EventRoutes.OrderCreated, new OrderCreatedPayload(
            Guid.NewGuid(),
            order.Id,
            order.ClientId,
            order.VehicleId,
            order.Services.Select(service => service.Description).ToList(),
            clock.UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ServiceOrderDto.From(order));
    }
}
