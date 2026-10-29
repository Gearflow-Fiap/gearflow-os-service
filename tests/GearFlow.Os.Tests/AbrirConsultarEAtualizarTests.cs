using System.Text.Json;
using FluentAssertions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Application.UseCases.AbrirOrdemServico;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Application.UseCases.CadastrarCliente;
using GearFlow.Os.Application.UseCases.CadastrarVeiculo;
using GearFlow.Os.Application.UseCases.ConsultarOrdemServico;
using GearFlow.Os.Application.UseCases.FinalizarOrdemServico;
using GearFlow.Os.Tests.Support;

namespace GearFlow.Os.Tests;

public class AbrirConsultarEAtualizarTests
{
    [Fact]
    public async Task Abertura_publica_order_created_e_a_consulta_devolve_o_historico()
    {
        var db = new FakeDatabase();
        var clock = new FixedClock(new DateTime(2026, 10, 18, 15, 0, 0, DateTimeKind.Utc));
        var client = await new CadastrarClienteHandler(db, db, clock)
            .Handle(new CadastrarClienteCommand("Ana Souza", "529.982.247-25", "ana@oficina.com", null), CancellationToken.None);
        var vehicle = await new CadastrarVeiculoHandler(db, db, db, clock)
            .Handle(new CadastrarVeiculoCommand(client.Value.Id, "ABC1D23", "Gol"), CancellationToken.None);

        var opened = await new AbrirOrdemServicoHandler(db, db, db, db, clock)
            .Handle(new AbrirOrdemServicoCommand(vehicle.Value.Id, ["Troca de óleo"]), CancellationToken.None);

        opened.IsSuccess.Should().BeTrue();
        opened.Value.Code.Should().Be(1);
        opened.Value.Status.Should().Be("Created");
        db.Published.Should().ContainSingle(item => item.RoutingKey == EventRoutes.OrderCreated);
        var payload = JsonSerializer.Deserialize<OrderCreatedPayload>(db.Published[0].Payload, OsJson.Options);
        payload!.ServiceOrderId.Should().Be(opened.Value.Id);

        var consulted = await new ConsultarOrdemServicoHandler(db).PorId(opened.Value.Id, CancellationToken.None);
        consulted.Value.History.Should().ContainSingle(item => item.Status == "Created" && item.Message == "Ordem de serviço aberta.");
    }

    [Fact]
    public async Task Evento_repetido_nao_duplica_o_historico()
    {
        var (db, orderId) = await OpenOrder();
        var status = new AtualizarStatusOsHandler(db, db, db, new FixedClock(DateTime.UtcNow));
        var messageId = Guid.NewGuid();

        (await status.QuandoOrcamentoGerado(messageId, orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await status.QuandoOrcamentoGerado(messageId, orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var consulted = await new ConsultarOrdemServicoHandler(db).PorId(orderId, CancellationToken.None);
        consulted.Value.History.Count(item => item.Status == "WaitingOrcamento").Should().Be(1);
    }

    private static async Task<(FakeDatabase Database, Guid OrderId)> OpenOrder()
    {
        var db = new FakeDatabase();
        var clock = new FixedClock(DateTime.UtcNow);
        var client = await new CadastrarClienteHandler(db, db, clock)
            .Handle(new CadastrarClienteCommand("Ana Souza", "529.982.247-25", "ana@oficina.com", null), CancellationToken.None);
        var vehicle = await new CadastrarVeiculoHandler(db, db, db, clock)
            .Handle(new CadastrarVeiculoCommand(client.Value.Id, "ABC1D23", "Gol"), CancellationToken.None);
        var opened = await new AbrirOrdemServicoHandler(db, db, db, db, clock)
            .Handle(new AbrirOrdemServicoCommand(vehicle.Value.Id, ["Troca de óleo"]), CancellationToken.None);
        return (db, opened.Value.Id);
    }
}
