using System.Diagnostics;
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
                await Topology.DeclareAsync(channel, settings, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var published = await PublishPendingAsync(channel, settings, stoppingToken);
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

    private async Task<int> PublishPendingAsync(IChannel channel, RabbitMqOptions settings, CancellationToken cancellationToken)
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
            // O span é filho do contexto gravado ao enfileirar (a requisição ou a mensagem que gerou isto), não de nada deste serviço em segundo plano.
            using var activity = OsTelemetry.StartPublish(
                OsTelemetry.ParseParent(message.TraceParent, message.TraceState),
                OutboxPublishing.Destination(message, settings.Exchange),
                message.MessageId,
                message.CorrelationId);
            var properties = OutboxPublishing.CreateProperties(message, activity);

            try
            {
                if (!string.IsNullOrWhiteSpace(message.Queue))
                {
                    await channel.QueueDeclareAsync(message.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
                    await channel.BasicPublishAsync("", message.Queue, false, properties, Encoding.UTF8.GetBytes(message.Payload), cancellationToken);
                }
                else
                {
                    var exchange = string.IsNullOrWhiteSpace(message.Exchange) ? settings.Exchange : message.Exchange;
                    await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
                    await channel.BasicPublishAsync(exchange, message.RoutingKey, false, properties, Encoding.UTF8.GetBytes(message.Payload), cancellationToken);
                }
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                throw;
            }

            message.ProcessedOn = DateTime.UtcNow;
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return pending.Count;
    }
}
