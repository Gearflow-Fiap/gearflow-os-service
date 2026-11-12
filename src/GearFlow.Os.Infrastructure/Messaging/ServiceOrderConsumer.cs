using System.Text;
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
        var json = Encoding.UTF8.GetString(delivery.Body.Span);
        var routingKey = string.IsNullOrWhiteSpace(delivery.RoutingKey) ? delivery.Exchange : delivery.RoutingKey;
        // Continua o trace de quem enviou (header traceparent do Billing ou Diagnostic-Id do MassTransit). Tudo que o handler enfileirar
        // enquanto esta mensagem é processada herda este span, então a Saga segue como um trace só.
        using var activity = OsTelemetry.StartConsume(OsTelemetry.ParentFromHeaders(delivery.BasicProperties.Headers, json), routingKey);
        try
        {
            IncomingSagaMessage? parsed;
            try
            {
                parsed = IncomingMessageParser.Parse(routingKey, json);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Mensagem ilegível em {Route}. Descartada.", routingKey);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            if (parsed is null)
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            activity?.SetTag("os.saga.event", parsed.Type);
            OsTelemetry.MarkServiceOrder(activity, parsed.ServiceOrderId);

            using var scope = scopes.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<AtualizarStatusOsHandler>();
            var result = await handler.Handle(parsed, cancellationToken);

            if (result.IsFailure && result.Error.Type == ErrorType.NotFound)
            {
                logger.LogWarning("OS {ServiceOrderId} não existe. Evento {Type} descartado.", parsed.ServiceOrderId, parsed.Type);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            if (result.IsFailure)
                logger.LogWarning("Evento {Type} da OS {ServiceOrderId} recusado: {Error}", parsed.Type, parsed.ServiceOrderId, result.Error.Description);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, exception.Message);
            logger.LogError(exception, "Falha ao processar evento {Route}.", delivery.RoutingKey);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }
}
