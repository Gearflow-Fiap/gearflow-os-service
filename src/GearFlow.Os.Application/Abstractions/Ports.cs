using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Orders;

namespace GearFlow.Os.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IOutbox
{
    void Enqueue(string routingKey, object payload);
}

public interface IInbox
{
    Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken);
    void MarkProcessed(Guid messageId, string type);
}

public interface IClientRepository
{
    Task AddAsync(Client client, CancellationToken cancellationToken);
    Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> DocumentExistsAsync(string document, CancellationToken cancellationToken);
}

public interface IVehicleRepository
{
    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken);
    Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> PlateExistsAsync(string plate, CancellationToken cancellationToken);
}

public sealed record ServiceOrderListFilter(int Page, int PageSize, bool IncludeClosed);

public sealed record ServiceOrderPage(IReadOnlyList<ServiceOrder> Items, int Page, int PageSize, int Total);

public interface IServiceOrderRepository
{
    Task AddAsync(ServiceOrder order, CancellationToken cancellationToken);
    Task<ServiceOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ServiceOrderPage> ListAsync(ServiceOrderListFilter filter, CancellationToken cancellationToken);
}
