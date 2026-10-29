using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Dtos;
using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Application.UseCases.CadastrarCliente;

public sealed record CadastrarClienteCommand(string Name, string Document, string Email, string? Phone);

public sealed class CadastrarClienteHandler(IClientRepository clients, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<ClientDto>> Handle(CadastrarClienteCommand command, CancellationToken cancellationToken)
    {
        var created = Client.Create(command.Name, command.Document, command.Email, command.Phone, clock.UtcNow);
        if (created.IsFailure)
            return Result.Failure<ClientDto>(created.Error);

        if (await clients.DocumentExistsAsync(created.Value.Document, cancellationToken))
            return Result.Failure<ClientDto>(Error.Conflict("Client.DocumentInUse", "Já existe um cliente com este documento."));

        await clients.AddAsync(created.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var client = created.Value;
        return Result.Success(new ClientDto(client.Id, client.Name, client.Document, client.Email, client.Phone, client.CreatedOn));
    }
}
