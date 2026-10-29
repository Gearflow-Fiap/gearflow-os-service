using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Dtos;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.ConsultarCadastro;

public sealed class ConsultarCadastroHandler(IClientRepository clients, IVehicleRepository vehicles)
{
    public async Task<Result<ClientDto>> Cliente(Guid id, CancellationToken cancellationToken)
    {
        var client = await clients.GetByIdAsync(id, cancellationToken);
        if (client is null)
            return Result.Failure<ClientDto>(Error.NotFound("Client.NotFound", "Cliente não encontrado."));
        return Result.Success(new ClientDto(client.Id, client.Name, client.Document, client.Email, client.Phone, client.CreatedOn));
    }

    public async Task<Result<VehicleDto>> Veiculo(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await vehicles.GetByIdAsync(id, cancellationToken);
        if (vehicle is null)
            return Result.Failure<VehicleDto>(Error.NotFound("Vehicle.NotFound", "Veículo não encontrado."));
        return Result.Success(new VehicleDto(vehicle.Id, vehicle.ClientId, vehicle.Plate, vehicle.Model, vehicle.CreatedOn));
    }
}
