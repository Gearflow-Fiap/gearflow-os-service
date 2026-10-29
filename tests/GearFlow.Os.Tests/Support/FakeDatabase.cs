using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Orders;

namespace GearFlow.Os.Tests.Support;

internal sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}

internal sealed class FakeDatabase : IClientRepository, IVehicleRepository, IServiceOrderRepository, IUnitOfWork, IOutbox, IInbox
{
    private readonly List<Client> _clients = [];
    private readonly List<Vehicle> _vehicles = [];
    private readonly List<ServiceOrder> _orders = [];
    private readonly HashSet<Guid> _inbox = [];
    private readonly List<PendingOutbox> _pendingOutbox = [];
    private int _nextCode = 1;

    public List<(string RoutingKey, string Payload)> Published { get; } = [];

    public Task AddAsync(Client client, CancellationToken cancellationToken)
    {
        _clients.Add(client);
        return Task.CompletedTask;
    }

    Task<Client?> IClientRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_clients.SingleOrDefault(client => client.Id == id));

    public Task<bool> DocumentExistsAsync(string document, CancellationToken cancellationToken) =>
        Task.FromResult(_clients.Any(client => client.Document == document));

    public Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken)
    {
        _vehicles.Add(vehicle);
        return Task.CompletedTask;
    }

    Task<Vehicle?> IVehicleRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_vehicles.SingleOrDefault(vehicle => vehicle.Id == id));

    public Task<bool> PlateExistsAsync(string plate, CancellationToken cancellationToken) =>
        Task.FromResult(_vehicles.Any(vehicle => vehicle.Plate == plate));

    public Task AddAsync(ServiceOrder order, CancellationToken cancellationToken)
    {
        _orders.Add(order);
        return Task.CompletedTask;
    }

    Task<ServiceOrder?> IServiceOrderRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.SingleOrDefault(order => order.Id == id));

    public Task<ServiceOrderPage> ListAsync(ServiceOrderListFilter filter, CancellationToken cancellationToken)
    {
        var query = _orders.AsEnumerable();
        if (!filter.IncludeClosed)
            query = query.Where(order => order.Status is not ServiceOrderStatus.Completed and not ServiceOrderStatus.Cancelled);

        var items = query.OrderBy(order => order.CreatedOn).ToList();
        var page = items.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        return Task.FromResult(new ServiceOrderPage(page, filter.Page, filter.PageSize, items.Count));
    }

    public void Enqueue(string routingKey, object payload) =>
        _pendingOutbox.Add(new PendingOutbox(routingKey, System.Text.Json.JsonSerializer.Serialize(payload, OsJson.Options)));

    public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken) =>
        Task.FromResult(_inbox.Contains(messageId));

    public void MarkProcessed(Guid messageId, string type) => _inbox.Add(messageId);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var order in _orders.Where(order => order.Code == 0))
            order.AssignCode(_nextCode++);
        Published.AddRange(_pendingOutbox.Select(item => (item.RoutingKey, item.Payload)));
        _pendingOutbox.Clear();
        return Task.CompletedTask;
    }

    private sealed record PendingOutbox(string RoutingKey, string Payload);
}
