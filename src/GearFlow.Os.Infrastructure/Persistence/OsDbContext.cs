using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GearFlow.Os.Infrastructure.Persistence;

public sealed class OsDbContext : DbContext
{
    public OsDbContext(DbContextOptions<OsDbContext> options) : base(options)
    {
        ChangeTracker.Tracked += OnTracked;
    }

    private static void OnTracked(object? sender, EntityTrackedEventArgs args)
    {
        if (!args.FromQuery && args.Entry.Metadata.IsOwned() && args.Entry.State == EntityState.Modified)
            args.Entry.State = EntityState.Added;
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ServiceOrder> ServiceOrders => Set<ServiceOrder>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(client =>
        {
            client.ToTable("clients");
            client.HasKey(item => item.Id);
            client.Property(item => item.Name).HasMaxLength(160).IsRequired();
            client.Property(item => item.Document).HasMaxLength(14).IsRequired();
            client.Property(item => item.Email).HasMaxLength(160).IsRequired();
            client.Property(item => item.Phone).HasMaxLength(30);
            client.HasIndex(item => item.Document).IsUnique();
        });

        modelBuilder.Entity<Vehicle>(vehicle =>
        {
            vehicle.ToTable("vehicles");
            vehicle.HasKey(item => item.Id);
            vehicle.Property(item => item.Plate).HasMaxLength(7).IsRequired();
            vehicle.Property(item => item.Model).HasMaxLength(120).IsRequired();
            vehicle.HasIndex(item => item.Plate).IsUnique();
            vehicle.HasOne<Client>().WithMany().HasForeignKey(item => item.ClientId);
        });

        modelBuilder.Entity<ServiceOrder>(order =>
        {
            order.ToTable("service_orders");
            order.HasKey(item => item.Id);
            order.Property(item => item.Code).UseIdentityAlwaysColumn();
            order.Property(item => item.Status).HasConversion<string>().HasMaxLength(40);
            order.HasIndex(item => item.Status);
            order.HasIndex(item => item.VehicleId);

            order.OwnsMany(item => item.Services, service =>
            {
                service.ToTable("service_order_services");
                service.WithOwner().HasForeignKey("service_order_id");
                service.HasKey(item => item.Id);
                service.Property(item => item.Description).HasMaxLength(200).IsRequired();
            });

            order.OwnsMany(item => item.History, history =>
            {
                history.ToTable("service_order_history");
                history.WithOwner().HasForeignKey("service_order_id");
                history.HasKey(item => item.Id);
                history.Property(item => item.Status).HasConversion<string>().HasMaxLength(40);
                history.Property(item => item.Message).HasMaxLength(300).IsRequired();
            });

            order.Navigation(item => item.Services).UsePropertyAccessMode(PropertyAccessMode.Field);
            order.Navigation(item => item.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("outbox_messages");
            message.HasKey(item => item.Id);
            message.Property(item => item.RoutingKey).HasMaxLength(80).IsRequired();
            message.Property(item => item.Payload).IsRequired();
            message.HasIndex(item => item.ProcessedOn);
        });

        modelBuilder.Entity<InboxMessage>(message =>
        {
            message.ToTable("inbox_messages");
            message.HasKey(item => item.Id);
            message.Property(item => item.Type).HasMaxLength(80).IsRequired();
        });
    }
}
