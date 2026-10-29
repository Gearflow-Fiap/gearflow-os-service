using System.Text;
using System.Text.Json;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Domain.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace GearFlow.Os.Infrastructure.Messaging;

public sealed class ServiceOrderConsumer(
    IServiceScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<ServiceOrderConsumer> logger) : BackgroundService
{
    private static readonly string[] Routes =
    [
        EventRoutes.OrcamentoGerado,
        EventRoutes.OrcamentoApproved,
        EventRoutes.OrcamentoRejected,
        EventRoutes.OperationCompleted
    ];

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
                await channel.QueueDeclareAsync(settings.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                foreach (var route in Routes)
                    await channel.QueueBindAsync(settings.Queue, settings.Exchange, route, cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(settings.Queue, autoAck: false, consumer, stoppingToken);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Consumidor sem conexão com o RabbitMQ. Nova tentativa em 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            var json = Encoding.UTF8.GetString(delivery.Body.Span);
            var message = JsonSerializer.Deserialize<ServiceOrderEvent>(json, OsJson.Options);
            if (message is null || message.ServiceOrderId == Guid.Empty)
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            var messageId = message.MessageId == Guid.Empty
                ? DeterministicId(delivery.RoutingKey, json)
                : message.MessageId;

            using var scope = scopes.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<AtualizarStatusOsHandler>();
            var result = delivery.RoutingKey switch
            {
                EventRoutes.OrcamentoGerado => await handler.QuandoOrcamentoGerado(messageId, message.ServiceOrderId, cancellationToken),
                EventRoutes.OrcamentoApproved => await handler.QuandoOrcamentoAprovado(messageId, message.ServiceOrderId, cancellationToken),
                EventRoutes.OrcamentoRejected => await handler.QuandoOrcamentoRejeitado(messageId, message.ServiceOrderId, cancellationToken),
                EventRoutes.OperationCompleted => await handler.QuandoOperacaoConcluida(messageId, message.ServiceOrderId, cancellationToken),
                _ => Result.Success()
            };

            if (result.IsFailure && result.Error.Type == ErrorType.NotFound)
            {
                logger.LogWarning("OS {ServiceOrderId} não existe. Evento {Route} descartado.", message.ServiceOrderId, delivery.RoutingKey);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            if (result.IsFailure)
                logger.LogWarning("Evento {Route} da OS {ServiceOrderId} recusado: {Error}", delivery.RoutingKey, message.ServiceOrderId, result.Error.Description);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao processar evento {Route}.", delivery.RoutingKey);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }

    private static Guid DeterministicId(string routingKey, string json)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(routingKey + json));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

internal static class RabbitConnection
{
    public static ConnectionFactory CreateFactory(RabbitMqOptions options) => new()
    {
        HostName = options.Host,
        Port = options.Port,
        UserName = options.Username,
        Password = options.Password
    };
}
