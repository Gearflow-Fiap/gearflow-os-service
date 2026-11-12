using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GearFlow.Os.Application.Messaging;

/// <summary>
/// Propaga o contexto de trace (W3C) do OS pelas mensagens do RabbitMQ, para que uma OS apareça como UM trace atravessando os serviços.
///
/// O OS publica pelo outbox, num serviço em segundo plano: quando a mensagem sai, o <see cref="Activity.Current"/> da requisição que a gerou
/// já acabou. Por isso o contexto é capturado ao enfileirar (<see cref="CaptureCurrent"/>), gravado na linha do outbox e só então
/// enviado como header (<see cref="PropagationHeaders"/>). Na entrada, o consumidor lê o header e continua o trace (<see cref="ParentFromHeaders"/>).
///
/// Dois headers carregam o mesmo valor, porque cada vizinho lê um:
/// <c>traceparent</c> (Billing, RabbitMQ.Client) e <c>Diagnostic-Id</c> (Inventory e Operation, MassTransit 8).
/// </summary>
public static class OsTelemetry
{
    /// <summary>Nome da <see cref="ActivitySource"/>; precisa estar em <c>AddSource</c> no OpenTelemetry da API.</summary>
    public const string Name = "GearFlow.Os";

    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";
    public const string MassTransitHeader = "Diagnostic-Id";
    public const string ServiceOrderTag = "gearflow.service_order.id";

    private const int MaxTraceStateLength = 512;

    public static readonly ActivitySource Source = new(Name);

    public readonly record struct TraceContext(string? TraceParent, string? TraceState);

    /// <summary>Contexto da atividade corrente (a requisição HTTP ou a mensagem em processamento); vazio se não houver.</summary>
    public static TraceContext CaptureCurrent()
    {
        if (Activity.Current is not { IdFormat: ActivityIdFormat.W3C, Id: { } traceParent } activity)
            return default;

        var traceState = activity.TraceStateString;
        return new TraceContext(traceParent, traceState is { Length: <= MaxTraceStateLength } ? traceState : null);
    }

    public static bool TryParse(string? traceParent, string? traceState, out ActivityContext context)
    {
        context = default;
        return !string.IsNullOrWhiteSpace(traceParent) && ActivityContext.TryParse(traceParent, traceState, true, out context);
    }

    public static ActivityContext ParseParent(string? traceParent, string? traceState) =>
        TryParse(traceParent, traceState, out var context) ? context : default;

    /// <summary>Headers AMQP que levam o contexto aos vizinhos. Vazio se o valor guardado não for um traceparent W3C válido.</summary>
    public static Dictionary<string, object?> PropagationHeaders(string? traceParent, string? traceState)
    {
        var headers = new Dictionary<string, object?>();
        if (!TryParse(traceParent, traceState, out _))
            return headers;

        headers[TraceParentHeader] = traceParent;
        headers[MassTransitHeader] = traceParent;
        if (!string.IsNullOrWhiteSpace(traceState))
            headers[TraceStateHeader] = traceState;
        return headers;
    }

    /// <summary>Header AMQP como texto. O RabbitMQ.Client entrega string longa como <c>byte[]</c>.</summary>
    public static string? HeaderAsString(object? value) => value switch
    {
        string text => text,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        _ => null
    };

    /// <summary>
    /// Pai do trace de uma mensagem recebida: header <c>traceparent</c> (Billing) ou <c>Diagnostic-Id</c> (MassTransit). Se o transporte
    /// não trouxer nenhum, tenta o objeto <c>headers</c> do envelope JSON do MassTransit. Sem nada válido, devolve vazio (o trace recomeça).
    /// </summary>
    public static ActivityContext ParentFromHeaders(IDictionary<string, object?>? headers, string? body = null)
    {
        var traceParent = Read(headers, TraceParentHeader) ?? Read(headers, MassTransitHeader);
        var traceState = Read(headers, TraceStateHeader);
        return TryParse(traceParent, traceState, out var context) ? context : ParentFromEnvelope(body);
    }

    public static ActivityContext ParentFromEnvelope(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("headers", out var headers)
                || headers.ValueKind != JsonValueKind.Object)
                return default;

            var traceParent = ReadString(headers, TraceParentHeader) ?? ReadString(headers, MassTransitHeader);
            return ParseParent(traceParent, ReadString(headers, TraceStateHeader));
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>Span de saída do outbox. É filho do contexto gravado ao enfileirar; sem listener (ninguém coletando traces), devolve null.</summary>
    public static Activity? StartPublish(ActivityContext parent, string destination, Guid messageId, Guid serviceOrderId)
    {
        var activity = Source.StartActivity("os.outbox.publish", ActivityKind.Producer, parent);
        if (activity is null)
            return null;

        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation.type", "publish");
        activity.SetTag("messaging.destination.name", destination);
        if (messageId != Guid.Empty)
            activity.SetTag("messaging.message.id", messageId.ToString("D"));
        MarkServiceOrder(activity, serviceOrderId);
        return activity;
    }

    /// <summary>Span de entrada de uma mensagem. É filho do contexto vindo do vizinho; o que o handler enfileirar depois continua este trace.</summary>
    public static Activity? StartConsume(ActivityContext parent, string route)
    {
        var activity = Source.StartActivity("os.consume", ActivityKind.Consumer, parent);
        if (activity is null)
            return null;

        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation.type", "process");
        activity.SetTag("messaging.destination.name", route);
        return activity;
    }

    public static void MarkServiceOrder(Activity? activity, Guid serviceOrderId)
    {
        if (activity is not null && serviceOrderId != Guid.Empty)
            activity.SetTag(ServiceOrderTag, serviceOrderId.ToString("D"));
    }

    private static string? Read(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value) ? HeaderAsString(value) : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
