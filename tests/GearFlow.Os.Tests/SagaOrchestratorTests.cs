using FluentAssertions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Application.Saga;
using GearFlow.Os.Application.UseCases.AbrirOrdemServico;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Application.UseCases.CadastrarCliente;
using GearFlow.Os.Application.UseCases.CadastrarVeiculo;
using GearFlow.Os.Application.UseCases.ConsultarCadastro;
using GearFlow.Os.Application.UseCases.ConsultarOrdemServico;
using GearFlow.Os.Application.UseCases.FinalizarOrdemServico;
using GearFlow.Os.Tests.Support;

namespace GearFlow.Os.Tests;

public class SagaOrchestratorTests
{
    [Fact]
    public async Task Diagnostico_com_pecas_reserva_cobra_executa_e_baixa_estoque()
    {
        var (db, orderId, clientId) = await OpenOrder();
        var clock = new FixedClock(DateTime.UtcNow);
        var handler = new AtualizarStatusOsHandler(db, db, db, new SagaOrchestrator(db, clock), db, clock);
        var partId = Guid.NewGuid();
        var snapshot = new DiagnosticSnapshot(
            Guid.NewGuid(),
            "BRL",
            "per-unit-v1",
            DateTimeOffset.UtcNow,
            15000,
            [
                new CommercialLine(Guid.NewGuid(), "Labor", Guid.NewGuid(), "Revisão", 1, 10000, 10000),
                new CommercialLine(Guid.NewGuid(), "Part", partId, "Filtro", 1, 5000, 5000)
            ]);

        var diagnosticJson = """
            {"message":{"snapshot":PLACEHOLDER}}
            """.Replace("PLACEHOLDER", System.Text.Json.JsonSerializer.Serialize(snapshot, OsJson.Options));

        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.DiagnosticFinished, Guid.NewGuid(), orderId, null, null, null, null, diagnosticJson), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        db.Published.Should().Contain(item => item.RoutingKey == "billing.GenerateBudget");

        var budgetId = Guid.NewGuid();
        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.BudgetGenerated, Guid.NewGuid(), orderId, budgetId, null, null, null, "{}"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.BudgetApproved, Guid.NewGuid(), orderId, budgetId, null, null, null, "{}"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        db.Published.Should().Contain(item => item.RoutingKey == SagaTopology.ReserveStockQueue);

        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.StockReserved, Guid.NewGuid(), orderId, null, null, null, null, "{}"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        db.Published.Should().Contain(item => item.RoutingKey == "billing.RequestPayment");

        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.PaymentConfirmed, Guid.NewGuid(), orderId, budgetId, Guid.NewGuid(), 150m, "BRL", "{}"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        db.Published.Should().Contain(item => item.RoutingKey == SagaTopology.StartExecutionQueue);

        (await handler.QuandoOperacaoConcluida(Guid.NewGuid(), orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        db.Published.Should().Contain(item => item.RoutingKey == SagaTopology.ConsumeStockQueue);

        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.StockConsumed, Guid.NewGuid(), orderId, null, null, null, null, "{}"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        (await new FinalizarOrdemServicoHandler(db, db, clock).Handle(orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var consulted = await new ConsultarOrdemServicoHandler(db).PorId(orderId, CancellationToken.None);
        consulted.Value.Status.Should().Be("Completed");

        var client = await new ConsultarCadastroHandler(db, db).Cliente(clientId, CancellationToken.None);
        client.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Pagamento_falho_libera_estoque_e_rejeicao_cancela_operacao()
    {
        var (db, orderId, _) = await OpenOrder();
        var clock = new FixedClock(DateTime.UtcNow);
        var handler = new AtualizarStatusOsHandler(db, db, db, new SagaOrchestrator(db, clock), db, clock);

        var snapshot = new DiagnosticSnapshot(Guid.NewGuid(), "BRL", "v1", DateTimeOffset.UtcNow, 100, [
            new CommercialLine(Guid.NewGuid(), "Part", Guid.NewGuid(), "Filtro", 1, 100, 100)
        ]);
        var json = "{\"message\":{\"snapshot\":" + System.Text.Json.JsonSerializer.Serialize(snapshot, OsJson.Options) + "}}";
        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.DiagnosticFinished, Guid.NewGuid(), orderId, null, null, null, null, json), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handler.QuandoOrcamentoGerado(Guid.NewGuid(), orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handler.QuandoOrcamentoAprovado(Guid.NewGuid(), orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.StockReserved, Guid.NewGuid(), orderId, null, null, null, null, "{}"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.PaymentFailed, Guid.NewGuid(), orderId, null, null, null, null, "{}"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        db.Published.Should().Contain(item => item.RoutingKey == SagaTopology.ReleaseStockQueue);

        var other = await OpenOrder();
        var otherHandler = new AtualizarStatusOsHandler(other.Database, other.Database, other.Database, new SagaOrchestrator(other.Database, clock), other.Database, clock);
        (await otherHandler.QuandoOrcamentoGerado(Guid.NewGuid(), other.OrderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await otherHandler.QuandoOrcamentoRejeitado(Guid.NewGuid(), other.OrderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        other.Database.Published.Should().Contain(item => item.RoutingKey == SagaTopology.CancelOperationQueue);

        var consulted = await new ConsultarOrdemServicoHandler(other.Database).PorId(other.OrderId, CancellationToken.None);
        consulted.Value.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Reposicao_dispara_nova_reserva_para_os_aguardando_pecas()
    {
        var (db, orderId, _) = await OpenOrder();
        var clock = new FixedClock(DateTime.UtcNow);
        var handler = new AtualizarStatusOsHandler(db, db, db, new SagaOrchestrator(db, clock), db, clock);
        var snapshot = new DiagnosticSnapshot(Guid.NewGuid(), "BRL", "v1", DateTimeOffset.UtcNow, 500, [
            new CommercialLine(Guid.NewGuid(), "Part", Guid.NewGuid(), "Pastilha", 2, 250, 500)
        ]);
        var json = "{\"message\":{\"snapshot\":" + System.Text.Json.JsonSerializer.Serialize(snapshot, OsJson.Options) + "}}";
        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.DiagnosticFinished, Guid.NewGuid(), orderId, null, null, null, null, json), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handler.QuandoOrcamentoGerado(Guid.NewGuid(), orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handler.QuandoOrcamentoAprovado(Guid.NewGuid(), orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.StockInsufficient, Guid.NewGuid(), orderId, null, null, null, null, "{}"), CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await handler.Handle(new IncomingSagaMessage(SagaEventTypes.StockReplenished, Guid.NewGuid(), Guid.Empty, null, null, null, null, "{}"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        db.Published.Count(item => item.RoutingKey == SagaTopology.ReserveStockQueue).Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Consulta_de_cadastro_e_listagem_funcionam()
    {
        var (db, orderId, clientId) = await OpenOrder();
        var list = await new ConsultarOrdemServicoHandler(db).Listar(1, 10, false, CancellationToken.None);
        list.Total.Should().Be(1);
        list.Items[0].Id.Should().Be(orderId);

        var client = await new ConsultarCadastroHandler(db, db).Cliente(clientId, CancellationToken.None);
        client.Value.Name.Should().Be("Ana Souza");
        (await new ConsultarCadastroHandler(db, db).Cliente(Guid.NewGuid(), CancellationToken.None)).IsFailure.Should().BeTrue();
        (await new ConsultarCadastroHandler(db, db).Veiculo(Guid.NewGuid(), CancellationToken.None)).IsFailure.Should().BeTrue();
        (await new ConsultarOrdemServicoHandler(db).PorId(Guid.NewGuid(), CancellationToken.None)).IsFailure.Should().BeTrue();
        (await new FinalizarOrdemServicoHandler(db, db, new FixedClock(DateTime.UtcNow)).Handle(Guid.NewGuid(), CancellationToken.None)).IsFailure.Should().BeTrue();
    }

    private static async Task<(FakeDatabase Database, Guid OrderId, Guid ClientId)> OpenOrder()
    {
        var db = new FakeDatabase();
        var clock = new FixedClock(DateTime.UtcNow);
        var client = await new CadastrarClienteHandler(db, db, clock)
            .Handle(new CadastrarClienteCommand("Ana Souza", "529.982.247-25", "ana@oficina.com", "11999999999"), CancellationToken.None);
        var vehicle = await new CadastrarVeiculoHandler(db, db, db, clock)
            .Handle(new CadastrarVeiculoCommand(client.Value.Id, "ABC1D23", "Gol"), CancellationToken.None);
        var opened = await new AbrirOrdemServicoHandler(db, db, new SagaOrchestrator(db, clock), db, clock)
            .Handle(new AbrirOrdemServicoCommand(vehicle.Value.Id, ["Troca de óleo"]), CancellationToken.None);
        return (db, opened.Value.Id, client.Value.Id);
    }
}
