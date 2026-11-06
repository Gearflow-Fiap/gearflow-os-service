using GearFlow.Os.Infrastructure.Messaging;
using GearFlow.Os.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GearFlow.Os.Api;

public sealed class DatabaseHealthCheck(OsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connected = await db.Database.CanConnectAsync(cancellationToken);
        return connected
            ? HealthCheckResult.Healthy("PostgreSQL")
            : HealthCheckResult.Unhealthy("PostgreSQL indisponível");
    }
}

public sealed class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory
            {
                HostName = options.Value.Host,
                Port = options.Value.Port,
                VirtualHost = string.IsNullOrWhiteSpace(options.Value.VirtualHost) ? "/" : options.Value.VirtualHost,
                UserName = options.Value.Username,
                Password = options.Value.Password,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
            };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ")
                : HealthCheckResult.Unhealthy("RabbitMQ fechado");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ indisponível", exception);
        }
    }
}
