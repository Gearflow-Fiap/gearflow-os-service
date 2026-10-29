using System.Text;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace GearFlow.Os.Infrastructure.Messaging;

public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var factory = RabbitConnection.CreateFactory(settings);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync(settings.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var published = await PublishPendingAsync(channel, settings.Exchange, stoppingToken);
                    if (published == 0)
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox sem conexão com o RabbitMQ. Nova tentativa em 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<int> PublishPendingAsync(IChannel channel, string exchange, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OsDbContext>();
        var pending = await db.OutboxMessages
            .Where(message => message.ProcessedOn == null)
            .OrderBy(message => message.CreatedOn)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var message in pending)
        {
            await channel.BasicPublishAsync(
                exchange,
                message.RoutingKey,
                Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken);
            message.ProcessedOn = DateTime.UtcNow;
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return pending.Count;
    }
}
