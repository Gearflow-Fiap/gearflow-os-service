using System.Diagnostics;
using System.Text;
using FluentAssertions;
using GearFlow.Os.Application.Messaging;

namespace GearFlow.Os.Tests;

/// <summary>
/// O contexto de trace precisa atravessar o RabbitMQ nos dois sentidos, senão uma OS aparece como traces soltos, um por serviço.
/// O que não dá para provar sem broker (a publicação real) fica coberto pela montagem dos headers e pelo caminho de ida e volta.
/// </summary>
public sealed class OsTelemetryTests : IDisposable
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string SpanId = "b7ad6b7169203331";
    private const string TraceParent = $"00-{TraceId}-{SpanId}-01";

    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name == OsTelemetry.Name,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
    };

    public OsTelemetryTests() => ActivitySource.AddActivityListener(listener);

    public void Dispose() => listener.Dispose();

    [Fact]
    public void Sem_atividade_corrente_nao_ha_contexto_a_capturar()
    {
        Activity.Current = null;

        var captured = OsTelemetry.CaptureCurrent();

        captured.TraceParent.Should().BeNull();
        captured.TraceState.Should().BeNull();
    }

    [Fact]
    public void Captura_o_traceparent_W3C_e_o_tracestate_da_atividade_corrente()
    {
        using var activity = OsTelemetry.Source.StartActivity("requisicao");
        activity!.TraceStateString = "vendor=valor";

        var captured = OsTelemetry.CaptureCurrent();

        captured.TraceParent.Should().Be(activity.Id);
        captured.TraceParent.Should().StartWith("00-");
        captured.TraceState.Should().Be("vendor=valor");
    }

    [Fact]
    public void Descarta_tracestate_maior_que_o_limite_da_coluna()
    {
        using var activity = OsTelemetry.Source.StartActivity("requisicao");
        activity!.TraceStateString = "vendor=" + new string('x', 600);

        OsTelemetry.CaptureCurrent().TraceState.Should().BeNull();
    }

    [Fact]
    public void Envia_o_mesmo_valor_em_traceparent_e_em_Diagnostic_Id()
    {
        var headers = OsTelemetry.PropagationHeaders(TraceParent, null);

        headers.Should().ContainKey("traceparent").WhoseValue.Should().Be(TraceParent);
        headers.Should().ContainKey("Diagnostic-Id").WhoseValue.Should().Be(TraceParent);
        headers.Should().NotContainKey("tracestate");
    }

    [Fact]
    public void Envia_o_tracestate_quando_existe()
    {
        OsTelemetry.PropagationHeaders(TraceParent, "vendor=valor")
            .Should().ContainKey("tracestate").WhoseValue.Should().Be("vendor=valor");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("lixo")]
    [InlineData("00-xyz-abc-01")]
    public void Nao_envia_header_quando_o_contexto_guardado_nao_e_um_traceparent_valido(string? stored)
    {
        OsTelemetry.PropagationHeaders(stored, "vendor=valor").Should().BeEmpty();
    }

    [Fact]
    public void Le_o_traceparent_como_texto_e_como_bytes()
    {
        var asText = new Dictionary<string, object?> { ["traceparent"] = TraceParent };
        var asBytes = new Dictionary<string, object?> { ["traceparent"] = Encoding.UTF8.GetBytes(TraceParent) };

        foreach (var headers in new[] { asText, asBytes })
        {
            var parent = OsTelemetry.ParentFromHeaders(headers);

            parent.TraceId.ToString().Should().Be(TraceId);
            parent.SpanId.ToString().Should().Be(SpanId);
            parent.IsRemote.Should().BeTrue();
        }
    }

    [Fact]
    public void Le_o_Diagnostic_Id_do_MassTransit_quando_nao_ha_traceparent()
    {
        var headers = new Dictionary<string, object?> { ["Diagnostic-Id"] = Encoding.UTF8.GetBytes(TraceParent) };

        OsTelemetry.ParentFromHeaders(headers).TraceId.ToString().Should().Be(TraceId);
    }

    [Fact]
    public void Prefere_o_traceparent_ao_Diagnostic_Id()
    {
        var headers = new Dictionary<string, object?>
        {
            ["traceparent"] = TraceParent,
            ["Diagnostic-Id"] = "00-11111111111111111111111111111111-2222222222222222-01"
        };

        OsTelemetry.ParentFromHeaders(headers).TraceId.ToString().Should().Be(TraceId);
    }

    [Fact]
    public void Cai_no_envelope_do_MassTransit_quando_o_transporte_nao_traz_headers()
    {
        var body = """{"messageId":"11111111-1111-1111-1111-111111111111","headers":{"Diagnostic-Id":"TRACEPARENT"},"message":{}}"""
            .Replace("TRACEPARENT", TraceParent);

        OsTelemetry.ParentFromHeaders(null, body).TraceId.ToString().Should().Be(TraceId);
        OsTelemetry.ParentFromHeaders(new Dictionary<string, object?>(), body).TraceId.ToString().Should().Be(TraceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("""{"message":{}}""")]
    [InlineData("""{"headers":"não é objeto"}""")]
    [InlineData("""{"headers":{"traceparent":"lixo"}}""")]
    [InlineData("[1,2,3]")]
    public void Sem_contexto_valido_o_trace_recomeca_em_vez_de_falhar(string? body)
    {
        var parent = OsTelemetry.ParentFromHeaders(new Dictionary<string, object?> { ["traceparent"] = "lixo" }, body);

        parent.TraceId.Should().Be(default(ActivityTraceId));
    }

    [Fact]
    public void Header_de_tipo_inesperado_e_ignorado()
    {
        OsTelemetry.HeaderAsString(42).Should().BeNull();
        OsTelemetry.HeaderAsString(null).Should().BeNull();
    }

    [Fact]
    public void O_span_de_publicacao_e_filho_do_contexto_gravado_ao_enfileirar()
    {
        var parent = OsTelemetry.ParseParent(TraceParent, null);
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using var activity = OsTelemetry.StartPublish(parent, "inventory-reserve-stock", messageId, orderId);

        activity.Should().NotBeNull();
        activity!.Kind.Should().Be(ActivityKind.Producer);
        activity.TraceId.ToString().Should().Be(TraceId);
        activity.ParentSpanId.ToString().Should().Be(SpanId);
        activity.GetTagItem("messaging.destination.name").Should().Be("inventory-reserve-stock");
        activity.GetTagItem("messaging.message.id").Should().Be(messageId.ToString("D"));
        activity.GetTagItem(OsTelemetry.ServiceOrderTag).Should().Be(orderId.ToString("D"));
    }

    [Fact]
    public void O_span_de_consumo_continua_o_trace_de_quem_enviou()
    {
        var headers = new Dictionary<string, object?> { ["traceparent"] = Encoding.UTF8.GetBytes(TraceParent) };

        using var activity = OsTelemetry.StartConsume(OsTelemetry.ParentFromHeaders(headers), "inventory.stock-reserved");

        activity!.Kind.Should().Be(ActivityKind.Consumer);
        activity.TraceId.ToString().Should().Be(TraceId);
        activity.ParentSpanId.ToString().Should().Be(SpanId);
    }

    [Fact]
    public void Mensagem_sem_contexto_inicia_um_trace_novo()
    {
        using var activity = OsTelemetry.StartConsume(OsTelemetry.ParentFromHeaders(null), "inventory.stock-reserved");

        activity.Should().NotBeNull();
        activity!.ParentId.Should().BeNull();
        activity.TraceId.ToString().Should().NotBe(TraceId);
    }

    [Fact]
    public void Sem_listener_nao_ha_span_e_o_contexto_gravado_ainda_e_repassado()
    {
        listener.Dispose();

        OsTelemetry.StartPublish(OsTelemetry.ParseParent(TraceParent, null), "fila", Guid.Empty, Guid.Empty).Should().BeNull();
        OsTelemetry.StartConsume(default, "rota").Should().BeNull();
        OsTelemetry.PropagationHeaders(TraceParent, null).Should().ContainKey("traceparent");
    }

    [Fact]
    public void A_Saga_continua_um_trace_so_da_requisicao_ate_o_consumo_da_resposta()
    {
        // 1. Requisição HTTP abre a OS e o handler enfileira o comando: o contexto é capturado.
        using var request = OsTelemetry.Source.StartActivity("POST /api/os");
        var stored = OsTelemetry.CaptureCurrent();
        request!.Stop();
        Activity.Current = null;

        // 2. O dispatcher, em segundo plano e sem Activity.Current, publica filho do contexto gravado e envia os headers.
        using var publish = OsTelemetry.StartPublish(OsTelemetry.ParseParent(stored.TraceParent, stored.TraceState), "inventory-reserve-stock", Guid.NewGuid(), Guid.NewGuid());
        var wire = OsTelemetry.PropagationHeaders(publish!.Id, publish.TraceStateString);
        publish.Stop();
        Activity.Current = null;

        // 3. O RabbitMQ entrega strings longas como byte[]; o vizinho (MassTransit) responde com o mesmo contexto no header.
        var delivered = wire.ToDictionary(pair => pair.Key, pair => (object?)Encoding.UTF8.GetBytes((string)pair.Value!));
        using var consume = OsTelemetry.StartConsume(OsTelemetry.ParentFromHeaders(delivered), "inventory.stock-reserved");

        // 4. O que o handler enfileirar agora (próximo passo da Saga) herda o mesmo trace.
        var next = OsTelemetry.CaptureCurrent();

        consume!.TraceId.Should().Be(request.TraceId);
        ActivityContext.TryParse(next.TraceParent, null, out var nextContext).Should().BeTrue();
        nextContext.TraceId.Should().Be(request.TraceId);
    }

    [Fact]
    public void Marcar_a_ordem_ignora_activity_nula_e_guid_vazio()
    {
        OsTelemetry.MarkServiceOrder(null, Guid.NewGuid());

        using var activity = OsTelemetry.Source.StartActivity("x");
        OsTelemetry.MarkServiceOrder(activity, Guid.Empty);

        activity!.GetTagItem(OsTelemetry.ServiceOrderTag).Should().BeNull();
    }
}
