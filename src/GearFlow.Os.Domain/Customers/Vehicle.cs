using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Domain.Customers;

public sealed class Vehicle : Entity
{
    public Guid ClientId { get; private set; }
    public string Plate { get; private set; } = "";
    public string Model { get; private set; } = "";
    public DateTime CreatedOn { get; private set; }

    private Vehicle()
    {
    }

    public static Result<Vehicle> Register(Guid clientId, string plate, string model, DateTime nowUtc)
    {
        if (clientId == Guid.Empty)
            return Result.Failure<Vehicle>(Error.Validation("Vehicle.ClientRequired", "Informe o cliente do veículo."));

        if (!LicensePlate.TryNormalize(plate, out var normalizedPlate, out var plateError))
            return Result.Failure<Vehicle>(plateError);

        model = (model ?? "").Trim();
        if (model.Length < 2)
            return Result.Failure<Vehicle>(Error.Validation("Vehicle.Model", "Informe o modelo do veículo."));

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Plate = normalizedPlate,
            Model = model,
            CreatedOn = nowUtc
        };
        return Result.Success(vehicle);
    }
}
