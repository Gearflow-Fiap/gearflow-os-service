using System.Diagnostics;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace GearFlow.Os.Infrastructure.Messaging;

public static class OutboxPublishing
{
    /// <summary>
    /// Propriedades AMQP de uma mensagem do outbox, com o contexto de trace nos headers <c>traceparent</c> e <c>Diagnostic-Id</c>.
    /// Usa o span de publicação quando há listener (o vizinho vira filho dele); senão repassa o contexto gravado ao enfileirar,
    /// para o trace não quebrar só porque ninguém está coletando spans neste processo.
    /// </summary>
    public static BasicProperties CreateProperties(OutboxMessage message, Activity? publish)
    {
        var properties = new BasicProperties
        {
            ContentType = string.IsNullOrWhiteSpace(message.ContentType) ? "application/json" : message.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.MessageId == Guid.Empty ? null : message.MessageId.ToString("D"),
            CorrelationId = message.CorrelationId == Guid.Empty ? null : message.CorrelationId.ToString("D")
        };

        var headers = OsTelemetry.PropagationHeaders(
            publish?.Id ?? message.TraceParent,
            publish is null ? message.TraceState : publish.TraceStateString);
        if (headers.Count > 0)
            properties.Headers = headers;

        return properties;
    }

    /// <summary>Destino lógico (fila, ou exchange quando não há fila) usado no span e na publicação.</summary>
    public static string Destination(OutboxMessage message, string defaultExchange) =>
        !string.IsNullOrWhiteSpace(message.Queue) ? message.Queue
        : string.IsNullOrWhiteSpace(message.Exchange) ? defaultExchange
        : message.Exchange;
}
