using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Dtos;
using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.CadastrarVeiculo;

public sealed record CadastrarVeiculoCommand(Guid ClientId, string Plate, string Model);

public sealed class CadastrarVeiculoHandler(
    IClientRepository clients,
    IVehicleRepository vehicles,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<VehicleDto>> Handle(CadastrarVeiculoCommand command, CancellationToken cancellationToken)
    {
        if (await clients.GetByIdAsync(command.ClientId, cancellationToken) is null)
            return Result.Failure<VehicleDto>(Error.NotFound("Client.NotFound", "Cliente não encontrado."));

        var created = Vehicle.Register(command.ClientId, command.Plate, command.Model, clock.UtcNow);
        if (created.IsFailure)
            return Result.Failure<VehicleDto>(created.Error);

        if (await vehicles.PlateExistsAsync(created.Value.Plate, cancellationToken))
            return Result.Failure<VehicleDto>(Error.Conflict("Vehicle.PlateInUse", "Já existe um veículo com esta placa."));

        await vehicles.AddAsync(created.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var vehicle = created.Value;
        return Result.Success(new VehicleDto(vehicle.Id, vehicle.ClientId, vehicle.Plate, vehicle.Model, vehicle.CreatedOn));
    }
}
