using FluentAssertions;
using GearFlow.Os.Application.Messaging;

namespace GearFlow.Os.Tests;

public class IncomingMessageParserTests
{
    [Fact]
    public void Interpreta_evento_legado_do_os()
    {
        var json = """{"messageId":"11111111-1111-1111-1111-111111111111","serviceOrderId":"22222222-2222-2222-2222-222222222222"}""";
        var parsed = IncomingMessageParser.Parse("budget.generated", json);

        parsed.Should().NotBeNull();
        parsed!.Type.Should().Be(SagaEventTypes.BudgetGenerated);
        parsed.ServiceOrderId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    [Fact]
    public void Interpreta_envelope_do_billing()
    {
        var json = """
            {"messageId":"11111111-1111-1111-1111-111111111111","messageType":"gearflow-budget-service.BudgetApproved","schemaVersion":1,"correlationId":"22222222-2222-2222-2222-222222222222","serviceOrderId":"22222222-2222-2222-2222-222222222222","producer":"gearflow-budget-service","payload":{"budgetId":"33333333-3333-3333-3333-333333333333"}}
            """;
        var parsed = IncomingMessageParser.Parse("gearflow-budget-service.BudgetApproved", json);

        parsed!.Type.Should().Be(SagaEventTypes.BudgetApproved);
        parsed.BudgetId.Should().Be(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    }

    [Fact]
    public void Interpreta_envelope_masstransit_do_operation()
    {
        var json = """
            {"messageId":"11111111-1111-1111-1111-111111111111","messageType":["urn:message:GearFlow.Contracts.Operation.V1:ExecutionFinished"],"message":{"correlationId":"22222222-2222-2222-2222-222222222222","messageId":"11111111-1111-1111-1111-111111111111"}}
            """;
        var parsed = IncomingMessageParser.Parse("operation.execution-finished", json);

        parsed!.Type.Should().Be(SagaEventTypes.ExecutionFinished);
        parsed.ServiceOrderId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    [Fact]
    public void Interpreta_recusa_de_comando_do_operation_com_nome_e_codigo()
    {
        var json = """
            {"messageId":"11111111-1111-1111-1111-111111111111","messageType":["urn:message:GearFlow.Contracts.Operation.V1:OperationCommandRejected"],"message":{"correlationId":"22222222-2222-2222-2222-222222222222","messageId":"11111111-1111-1111-1111-111111111111","commandName":"StartExecution","code":"snapshot.mismatch"}}
            """;
        var parsed = IncomingMessageParser.Parse("operation.command-rejected", json);

        parsed!.Type.Should().Be(SagaEventTypes.OperationCommandRejected);
        parsed.CommandName.Should().Be("StartExecution");
        parsed.Code.Should().Be("snapshot.mismatch");
    }

    [Theory]
    [InlineData("urn:message:GearFlow.Contracts.Operation.V1:OperationCancellationRejected", "OperationCancellationRejected")]
    [InlineData("urn:message:GearFlow.Contracts:StockReleased", "StockReleased")]
    [InlineData("urn:message:GearFlow.Contracts.Operation.V1:OperationCanceled", "OperationCanceled")]
    public void Interpreta_tipos_masstransit(string urn, string expected)
    {
        var json = "{\"messageType\":[\"" + urn + "\"],\"message\":{\"correlationId\":\"22222222-2222-2222-2222-222222222222\"}}";

        IncomingMessageParser.Parse("", json)!.Type.Should().Be(expected);
    }

    [Theory]
    [InlineData("BudgetCanceled")]
    [InlineData("PaymentRequested")]
    [InlineData("BudgetGenerationFailed")]
    [InlineData("PaymentRefunded")]
    public void Interpreta_eventos_do_billing(string evento)
    {
        var json = "{\"messageType\":\"gearflow-budget-service." + evento + "\",\"serviceOrderId\":\"22222222-2222-2222-2222-222222222222\",\"payload\":{}}";

        IncomingMessageParser.Parse("gearflow-budget-service." + evento, json)!.Type.Should().Be(evento);
    }

    [Fact]
    public void Aprovacao_do_billing_traz_total_e_moeda()
    {
        var json = """
            {"messageType":"gearflow-budget-service.BudgetApproved","serviceOrderId":"22222222-2222-2222-2222-222222222222","payload":{"budgetId":"33333333-3333-3333-3333-333333333333","totalAmount":149.95,"currency":"BRL"}}
            """;
        var parsed = IncomingMessageParser.Parse("gearflow-budget-service.BudgetApproved", json)!;

        parsed.Amount.Should().Be(149.95m);
        parsed.Currency.Should().Be("BRL");
    }
}
