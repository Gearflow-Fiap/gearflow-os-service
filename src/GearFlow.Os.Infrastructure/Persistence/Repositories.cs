using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Os.Infrastructure.Persistence;

public sealed class ClientRepository(OsDbContext db) : IClientRepository
{
    public Task AddAsync(Client client, CancellationToken cancellationToken)
    {
        db.Clients.Add(client);
        return Task.CompletedTask;
    }

    public Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Clients.FirstOrDefaultAsync(client => client.Id == id, cancellationToken);

    public Task<bool> DocumentExistsAsync(string document, CancellationToken cancellationToken) =>
        db.Clients.AnyAsync(client => client.Document == document, cancellationToken);
}

public sealed class VehicleRepository(OsDbContext db) : IVehicleRepository
{
    public Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken)
    {
        db.Vehicles.Add(vehicle);
        return Task.CompletedTask;
    }

    public Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Vehicles.FirstOrDefaultAsync(vehicle => vehicle.Id == id, cancellationToken);

    public Task<bool> PlateExistsAsync(string plate, CancellationToken cancellationToken) =>
        db.Vehicles.AnyAsync(vehicle => vehicle.Plate == plate, cancellationToken);
}

public sealed class ServiceOrderRepository(OsDbContext db) : IServiceOrderRepository
{
    public Task AddAsync(ServiceOrder order, CancellationToken cancellationToken)
    {
        db.ServiceOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task<ServiceOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.ServiceOrders.FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    public async Task<ServiceOrderPage> ListAsync(ServiceOrderListFilter filter, CancellationToken cancellationToken)
    {
        var query = db.ServiceOrders.AsQueryable();
        if (!filter.IncludeClosed)
        {
            query = query.Where(order =>
                order.Status != ServiceOrderStatus.Completed &&
                order.Status != ServiceOrderStatus.Cancelled);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(order => order.CreatedOn)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new ServiceOrderPage(items, filter.Page, filter.PageSize, total);
    }
}
