using System.Text.Json;
using FluentAssertions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Application.Saga;
using GearFlow.Os.Application.UseCases.AbrirOrdemServico;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Application.UseCases.CadastrarCliente;
using GearFlow.Os.Application.UseCases.CadastrarVeiculo;
using GearFlow.Os.Application.UseCases.ConsultarOrdemServico;
using GearFlow.Os.Tests.Support;

namespace GearFlow.Os.Tests;

public class AbrirConsultarEAtualizarTests
{
    [Fact]
    public async Task Abertura_publica_order_created_start_diagnostic_e_a_consulta_devolve_o_historico()
    {
        var db = new FakeDatabase();
        var clock = new FixedClock(new DateTime(2026, 10, 18, 15, 0, 0, DateTimeKind.Utc));
        var client = await new CadastrarClienteHandler(db, db, clock)
            .Handle(new CadastrarClienteCommand("Ana Souza", "529.982.247-25", "ana@oficina.com", null), CancellationToken.None);
        var vehicle = await new CadastrarVeiculoHandler(db, db, db, clock)
            .Handle(new CadastrarVeiculoCommand(client.Value.Id, "ABC1D23", "Gol"), CancellationToken.None);

        var opened = await new AbrirOrdemServicoHandler(db, db, new SagaOrchestrator(db, clock), db, clock)
            .Handle(new AbrirOrdemServicoCommand(vehicle.Value.Id, [new(Guid.NewGuid(), "Troca de óleo")]), CancellationToken.None);

        opened.IsSuccess.Should().BeTrue();
        opened.Value.Code.Should().Be(1);
        opened.Value.Status.Should().Be("Created");
        db.Published.Should().Contain(item => item.RoutingKey == EventRoutes.OrderCreatedV1);
        db.Published.Should().NotContain(item => item.RoutingKey == "os.created");
        db.Published.Should().Contain(item => item.RoutingKey == SagaTopology.StartDiagnosticQueue);
        var payload = JsonSerializer.Deserialize<OrderCreatedPayload>(
            db.Published.First(item => item.RoutingKey == EventRoutes.OrderCreatedV1).Payload, OsJson.Options);
        payload!.ServiceOrderId.Should().Be(opened.Value.Id);

        var consulted = await new ConsultarOrdemServicoHandler(db).PorId(opened.Value.Id, CancellationToken.None);
        consulted.Value.History.Should().ContainSingle(item => item.Status == "Created" && item.Message == "Ordem de serviço aberta.");
    }

    [Fact]
    public async Task Evento_repetido_nao_duplica_o_historico()
    {
        var (db, orderId) = await OpenOrder();
        var status = Handler(db);
        var messageId = Guid.NewGuid();

        (await status.QuandoOrcamentoGerado(messageId, orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await status.QuandoOrcamentoGerado(messageId, orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var consulted = await new ConsultarOrdemServicoHandler(db).PorId(orderId, CancellationToken.None);
        consulted.Value.History.Count(item => item.Status == "WaitingOrcamento").Should().Be(1);
    }

    [Fact]
    public async Task Aprovacao_sem_pecas_dispara_cobranca()
    {
        var (db, orderId) = await OpenOrder();
        var status = Handler(db);
        (await status.QuandoOrcamentoGerado(Guid.NewGuid(), orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await status.QuandoOrcamentoAprovado(Guid.NewGuid(), orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();

        db.Published.Should().Contain(item => item.RoutingKey == "billing.RequestPayment");
        var consulted = await new ConsultarOrdemServicoHandler(db).PorId(orderId, CancellationToken.None);
        consulted.Value.Status.Should().Be("AwaitingPayment");
    }

    private static AtualizarStatusOsHandler Handler(FakeDatabase db) =>
        new(db, db, db, new SagaOrchestrator(db, new FixedClock(DateTime.UtcNow)), db, new FixedClock(DateTime.UtcNow));

    private static async Task<(FakeDatabase Database, Guid OrderId)> OpenOrder()
    {
        var db = new FakeDatabase();
        var clock = new FixedClock(DateTime.UtcNow);
        var client = await new CadastrarClienteHandler(db, db, clock)
            .Handle(new CadastrarClienteCommand("Ana Souza", "529.982.247-25", "ana@oficina.com", null), CancellationToken.None);
        var vehicle = await new CadastrarVeiculoHandler(db, db, db, clock)
            .Handle(new CadastrarVeiculoCommand(client.Value.Id, "ABC1D23", "Gol"), CancellationToken.None);
        var opened = await new AbrirOrdemServicoHandler(db, db, new SagaOrchestrator(db, clock), db, clock)
            .Handle(new AbrirOrdemServicoCommand(vehicle.Value.Id, [new(Guid.NewGuid(), "Troca de óleo")]), CancellationToken.None);
        return (db, opened.Value.Id);
    }
}
