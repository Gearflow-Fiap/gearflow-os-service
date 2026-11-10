using GearFlow.Os.Application.Messaging;
using RabbitMQ.Client;

namespace GearFlow.Os.Infrastructure.Messaging;

internal static class Topology
{
    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(settings.Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(settings.CommandsExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(settings.BillingEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(settings.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueBindAsync(settings.Queue, settings.BillingEventsExchange, "gearflow-budget-service.*", cancellationToken: cancellationToken);

        foreach (var exchange in SagaTopology.OperationEventExchanges.Concat(SagaTopology.InventoryEventExchanges))
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(settings.Queue, exchange, "", cancellationToken: cancellationToken);
        }
    }
}

internal static class RabbitConnection
{
    public static ConnectionFactory CreateFactory(RabbitMqOptions options) => new()
    {
        HostName = options.Host,
        Port = options.Port,
        VirtualHost = string.IsNullOrWhiteSpace(options.VirtualHost) ? "/" : options.VirtualHost,
        UserName = options.Username,
        Password = options.Password
    };
}
