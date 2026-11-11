using System.Text.Json;
using FluentAssertions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Application.Saga;
using GearFlow.Os.Application.UseCases.AbrirOrdemServico;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Application.UseCases.CadastrarCliente;
using GearFlow.Os.Application.UseCases.CadastrarVeiculo;
using GearFlow.Os.Application.UseCases.CancelarOrdemServico;
using GearFlow.Os.Application.UseCases.ConsultarOrdemServico;
using GearFlow.Os.Domain.Primitives;
using GearFlow.Os.Tests.Support;

namespace GearFlow.Os.Tests;

/// <summary>Caminhos de falha da Saga: cada falha cancela a OS e dispara as compensações uma única vez.</summary>
public class SagaCompensacaoTests
{
    private const string CancelBudget = "billing.CancelBudget";
    private const string RefundPayment = "billing.RefundPayment";
    private const string RequestPayment = "billing.RequestPayment";

    [Fact]
    public async Task Falha_na_geracao_do_orcamento_cancela_a_os_e_a_operacao()
    {
        var saga = await Scenario.Open();

        (await saga.Send(SagaEventTypes.BudgetGenerationFailed)).IsSuccess.Should().BeTrue();

        (await saga.Status()).Should().Be("Cancelled");
        saga.Count(SagaTopology.CancelOperationQueue).Should().Be(1);
        saga.Count(CancelBudget).Should().Be(0);
        saga.Count(SagaTopology.ReleaseStockQueue).Should().Be(0);
    }

    [Fact]
    public async Task Pagamento_recusado_cancela_a_os_libera_estoque_e_cancela_o_orcamento_uma_vez()
    {
        var saga = await Scenario.Open();
        var budgetId = Guid.NewGuid();
        await saga.ReachAwaitingPayment(budgetId, withPart: true);

        (await saga.Send(SagaEventTypes.PaymentFailed, budgetId)).IsSuccess.Should().BeTrue();
        (await saga.Send(SagaEventTypes.PaymentFailed, budgetId)).IsSuccess.Should().BeTrue();

        (await saga.Status()).Should().Be("Cancelled");
        saga.Count(SagaTopology.ReleaseStockQueue).Should().Be(1);
        saga.Count(CancelBudget).Should().Be(1);
        saga.Count(SagaTopology.CancelOperationQueue).Should().Be(1);
        saga.Count(RefundPayment).Should().Be(0);
    }

    [Fact]
    public async Task Recusa_do_StartExecution_estorna_o_pagamento_e_libera_o_estoque()
    {
        var saga = await Scenario.Open();
        var budgetId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        await saga.ReachAwaitingPayment(budgetId, withPart: true);
        (await saga.Send(SagaEventTypes.PaymentConfirmed, budgetId, paymentId)).IsSuccess.Should().BeTrue();

        (await saga.Send(SagaEventTypes.OperationCommandRejected, commandName: "StartExecution", code: "snapshot.mismatch"))
            .IsSuccess.Should().BeTrue();

        (await saga.Status()).Should().Be("Cancelled");
        saga.Count(SagaTopology.ReleaseStockQueue).Should().Be(1);
        saga.Count(RefundPayment).Should().Be(1);
        saga.Count(CancelBudget).Should().Be(0);
        saga.Payloads(RefundPayment).Single().Should().Contain(paymentId.ToString());
        (await saga.History()).Should().Contain(message => message.Contains("StartExecution") && message.Contains("snapshot.mismatch"));
    }

    [Fact]
    public async Task Operacao_cancelada_pelo_operation_compensa_sem_reenviar_o_cancelamento()
    {
        var saga = await Scenario.Open();
        var budgetId = Guid.NewGuid();
        (await saga.Send(SagaEventTypes.BudgetGenerated, budgetId)).IsSuccess.Should().BeTrue();

        (await saga.Send(SagaEventTypes.OperationCanceled)).IsSuccess.Should().BeTrue();

        (await saga.Status()).Should().Be("Cancelled");
        saga.Count(SagaTopology.CancelOperationQueue).Should().Be(0);
        saga.Count(CancelBudget).Should().Be(1);
    }

    [Fact]
    public async Task Confirmacao_de_cancelamento_e_recusa_de_cancelamento_so_vao_para_o_historico()
    {
        var saga = await Scenario.Open();
        (await saga.Send(SagaEventTypes.BudgetRejected)).IsSuccess.Should().BeTrue();
        var published = saga.Total;

        (await saga.Send(SagaEventTypes.OperationCanceled)).IsSuccess.Should().BeTrue();
        (await saga.Send(SagaEventTypes.OperationCommandRejected, commandName: "CancelOperation", code: "already-finished")).IsSuccess.Should().BeTrue();
        (await saga.Send(SagaEventTypes.OperationCancellationRejected, code: "execution-started")).IsSuccess.Should().BeTrue();

        saga.Total.Should().Be(published);
        var history = await saga.History();
        history.Should().Contain("Cancelamento confirmado pelo Operation.");
        history.Should().Contain(message => message.Contains("already-finished"));
        history.Should().Contain(message => message.Contains("execution-started"));
    }

    [Fact]
    public async Task Eventos_de_confirmacao_das_compensacoes_entram_no_historico()
    {
        var saga = await Scenario.Open();

        (await saga.Send(SagaEventTypes.PaymentRequested)).IsSuccess.Should().BeTrue();
        (await saga.Send(SagaEventTypes.StockReleased)).IsSuccess.Should().BeTrue();
        (await saga.Send(SagaEventTypes.PaymentRefunded)).IsSuccess.Should().BeTrue();
        (await saga.Send(SagaEventTypes.BudgetCanceled)).IsSuccess.Should().BeTrue();

        var history = await saga.History();
        history.Should().Contain("Cobrança gerada. Aguardando o pagamento do cliente.");
        history.Should().Contain("Reserva de estoque liberada pelo Inventory.");
        history.Should().Contain("Pagamento estornado pelo Billing.");
        history.Should().Contain("Orçamento cancelado no Billing.");
        (await saga.Status()).Should().Be("Created");
    }

    [Fact]
    public async Task Cobranca_usa_o_total_aprovado_pelo_billing()
    {
        var saga = await Scenario.Open();
        var budgetId = Guid.NewGuid();
        (await saga.Send(SagaEventTypes.BudgetGenerated, budgetId)).IsSuccess.Should().BeTrue();

        (await saga.Send(SagaEventTypes.BudgetApproved, budgetId, amount: 149.95m, currency: "brl")).IsSuccess.Should().BeTrue();

        var payload = saga.Payloads(RequestPayment).Single();
        using var document = JsonDocument.Parse(payload);
        var body = document.RootElement.GetProperty("payload");
        body.GetProperty("amount").GetDecimal().Should().Be(149.95m);
        body.GetProperty("currency").GetString().Should().Be("BRL");
    }

    [Fact]
    public async Task Falha_depois_da_execucao_concluida_nao_cancela_a_os()
    {
        var saga = await Scenario.Open();
        var budgetId = Guid.NewGuid();
        await saga.ReachAwaitingPayment(budgetId, withPart: false);
        (await saga.Send(SagaEventTypes.PaymentConfirmed, budgetId, Guid.NewGuid())).IsSuccess.Should().BeTrue();
        (await saga.Send(SagaEventTypes.ExecutionFinished)).IsSuccess.Should().BeTrue();
        var published = saga.Total;

        (await saga.Send(SagaEventTypes.OperationCanceled)).IsSuccess.Should().BeTrue();

        (await saga.Status()).Should().Be("InProgress");
        saga.Total.Should().Be(published);
        (await saga.History()).Should().Contain(message => message.StartsWith("Falha recebida sem cancelamento"));
    }

    [Fact]
    public async Task Recepcao_cancela_os_parada_e_dispara_compensacoes()
    {
        var saga = await Scenario.Open();
        var budgetId = Guid.NewGuid();
        (await saga.Send(SagaEventTypes.BudgetGenerated, budgetId)).IsSuccess.Should().BeTrue();
        var handler = new CancelarOrdemServicoHandler(saga.Db, new SagaOrchestrator(saga.Db, saga.Clock), saga.Db, saga.Clock);

        (await handler.Handle(saga.OrderId, "cliente desistiu", CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await handler.Handle(saga.OrderId, null, CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await saga.Status()).Should().Be("Cancelled");
        saga.Count(SagaTopology.CancelOperationQueue).Should().Be(1);
        saga.Count(CancelBudget).Should().Be(1);
        (await saga.History()).Should().Contain("OS cancelada: cliente desistiu");

        var missing = await handler.Handle(Guid.NewGuid(), null, CancellationToken.None);
        missing.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Recepcao_nao_cancela_depois_da_execucao()
    {
        var saga = await Scenario.Open();
        var budgetId = Guid.NewGuid();
        await saga.ReachAwaitingPayment(budgetId, withPart: false);
        (await saga.Send(SagaEventTypes.PaymentConfirmed, budgetId, Guid.NewGuid())).IsSuccess.Should().BeTrue();
        (await saga.Send(SagaEventTypes.ExecutionFinished)).IsSuccess.Should().BeTrue();

        var result = await new CancelarOrdemServicoHandler(saga.Db, new SagaOrchestrator(saga.Db, saga.Clock), saga.Db, saga.Clock)
            .Handle(saga.OrderId, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Os.ExecutionFinished");
    }

    private sealed class Scenario
    {
        private Scenario(FakeDatabase db, FixedClock clock, Guid orderId)
        {
            Db = db;
            Clock = clock;
            OrderId = orderId;
        }

        public FakeDatabase Db { get; }
        public FixedClock Clock { get; }
        public Guid OrderId { get; }
        public int Total => Db.Published.Count;

        public static async Task<Scenario> Open()
        {
            var db = new FakeDatabase();
            var clock = new FixedClock(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
            var client = await new CadastrarClienteHandler(db, db, clock)
                .Handle(new CadastrarClienteCommand("Ana Souza", "529.982.247-25", "ana@oficina.com", null), CancellationToken.None);
            var vehicle = await new CadastrarVeiculoHandler(db, db, db, clock)
                .Handle(new CadastrarVeiculoCommand(client.Value.Id, "ABC1D23", "Gol"), CancellationToken.None);
            var opened = await new AbrirOrdemServicoHandler(db, db, new SagaOrchestrator(db, clock), db, clock)
                .Handle(new AbrirOrdemServicoCommand(vehicle.Value.Id, [new(Guid.NewGuid(), "Troca de pastilhas")]), CancellationToken.None);
            return new Scenario(db, clock, opened.Value.Id);
        }

        public async Task ReachAwaitingPayment(Guid budgetId, bool withPart)
        {
            CommercialLine[] lines = withPart
                ?
                [
                    new CommercialLine(Guid.NewGuid(), "Labor", Guid.NewGuid(), "Troca", 1, 8000, 8000),
                    new CommercialLine(Guid.NewGuid(), "Part", Guid.NewGuid(), "Pastilha", 1, 12000, 12000)
                ]
                : [new CommercialLine(Guid.NewGuid(), "Labor", Guid.NewGuid(), "Troca", 1, 8000, 8000)];
            var total = lines.Sum(line => line.LineTotalCents);
            var snapshot = new DiagnosticSnapshot(Guid.NewGuid(), "BRL", "v1", DateTimeOffset.UtcNow, total, lines);
            var json = "{\"message\":{\"snapshot\":" + JsonSerializer.Serialize(snapshot, OsJson.Options) + "}}";

            (await Send(SagaEventTypes.DiagnosticFinished, raw: json)).IsSuccess.Should().BeTrue();
            (await Send(SagaEventTypes.BudgetGenerated, budgetId)).IsSuccess.Should().BeTrue();
            (await Send(SagaEventTypes.BudgetApproved, budgetId, amount: total / 100m, currency: "BRL")).IsSuccess.Should().BeTrue();
            if (withPart)
                (await Send(SagaEventTypes.StockReserved)).IsSuccess.Should().BeTrue();
            (await Status()).Should().Be("AwaitingPayment");
        }

        public Task<Result> Send(
            string type,
            Guid? budgetId = null,
            Guid? paymentId = null,
            decimal? amount = null,
            string? currency = null,
            string raw = "{}",
            string? commandName = null,
            string? code = null) =>
            new AtualizarStatusOsHandler(Db, Db, Db, new SagaOrchestrator(Db, Clock), Db, Clock).Handle(
                new IncomingSagaMessage(type, Guid.NewGuid(), OrderId, budgetId, paymentId, amount, currency, raw, commandName, code),
                CancellationToken.None);

        public int Count(string routingKey) => Db.Published.Count(item => item.RoutingKey == routingKey);

        public IReadOnlyList<string> Payloads(string routingKey) =>
            Db.Published.Where(item => item.RoutingKey == routingKey).Select(item => item.Payload).ToList();

        public async Task<string> Status() =>
            (await new ConsultarOrdemServicoHandler(Db).PorId(OrderId, CancellationToken.None)).Value.Status;

        public async Task<IReadOnlyList<string>> History() =>
            (await new ConsultarOrdemServicoHandler(Db).PorId(OrderId, CancellationToken.None)).Value.History
                .Select(item => item.Message)
                .ToList();
    }
}
