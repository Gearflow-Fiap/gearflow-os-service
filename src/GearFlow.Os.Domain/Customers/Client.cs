using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Domain.Customers;

public sealed class Client : Entity
{
    public string Name { get; private set; } = "";
    public string Document { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string? Phone { get; private set; }
    public DateTime CreatedOn { get; private set; }

    private Client()
    {
    }

    public static Result<Client> Create(string name, string document, string email, string? phone, DateTime nowUtc)
    {
        name = (name ?? "").Trim();
        if (name.Length < 2)
            return Result.Failure<Client>(Error.Validation("Client.Name", "Informe o nome do cliente."));

        if (!DocumentNumber.TryNormalize(document, out var normalizedDocument, out var documentError))
            return Result.Failure<Client>(documentError);

        email = (email ?? "").Trim();
        if (email.Length < 3 || !email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
            return Result.Failure<Client>(Error.Validation("Client.Email", "E-mail inválido."));

        var client = new Client
        {
            Id = Guid.NewGuid(),
            Name = name,
            Document = normalizedDocument,
            Email = email,
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            CreatedOn = nowUtc
        };
        return Result.Success(client);
    }
}
