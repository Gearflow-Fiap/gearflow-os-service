using FluentAssertions;
using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Orders;

namespace GearFlow.Os.Tests;

public class ServiceOrderTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Abertura_grava_status_inicial_e_o_historico()
    {
        var order = ServiceOrder.Open(Guid.NewGuid(), Guid.NewGuid(), ["Troca de óleo"], Now).Value;

        order.Status.Should().Be(ServiceOrderStatus.Created);
        order.History.Should().ContainSingle(item => item.Status == ServiceOrderStatus.Created);
        order.Services.Select(service => service.Description).Should().Equal("Troca de óleo");
    }

    [Fact]
    public void Abertura_sem_servico_falha()
    {
        var result = ServiceOrder.Open(Guid.NewGuid(), Guid.NewGuid(), ["  "], Now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Os.NoServices");
    }

    [Fact]
    public void Pagamento_passa_por_paid_e_para_em_execucao()
    {
        var order = Opened();
        order.ApplyOrcamentoGerado(Now);
        order.ApplyOrcamentoApproved(Now);

        order.Status.Should().Be(ServiceOrderStatus.InProgress);
        order.History.Select(item => item.Status).Should().Equal(
            ServiceOrderStatus.Created,
            ServiceOrderStatus.WaitingOrcamento,
            ServiceOrderStatus.Paid,
            ServiceOrderStatus.InProgress);
    }

    [Fact]
    public void Rejeicao_cancela_e_uma_segunda_rejeicao_nao_duplica_historico()
    {
        var order = Opened();
        order.ApplyOrcamentoGerado(Now);
        order.ApplyOrcamentoRejected(Now);
        var count = order.History.Count;
        order.ApplyOrcamentoRejected(Now);

        order.Status.Should().Be(ServiceOrderStatus.Cancelled);
        order.History.Should().HaveCount(count);
    }

    [Fact]
    public void Entrega_so_acontece_depois_da_operacao()
    {
        var order = Opened();
        order.ApplyOrcamentoGerado(Now);
        order.ApplyOrcamentoApproved(Now);

        order.Deliver(Now).IsFailure.Should().BeTrue();
        order.ApplyOperationCompleted(Now);
        order.Deliver(Now).IsSuccess.Should().BeTrue();
        order.Status.Should().Be(ServiceOrderStatus.Completed);
        order.Deliver(Now).IsSuccess.Should().BeTrue();
        order.History.Count(item => item.Status == ServiceOrderStatus.Completed).Should().Be(1);
    }

    [Fact]
    public void Documento_e_placa_validos_sao_normalizados()
    {
        DocumentNumber.TryNormalize("529.982.247-25", out var document, out _).Should().BeTrue();
        document.Should().Be("52998224725");
        LicensePlate.TryNormalize("abc-1d23", out var plate, out _).Should().BeTrue();
        plate.Should().Be("ABC1D23");
        LicensePlate.TryNormalize("abc1234", out var oldPlate, out _).Should().BeTrue();
        oldPlate.Should().Be("ABC1234");
    }

    private static ServiceOrder Opened() =>
        ServiceOrder.Open(Guid.NewGuid(), Guid.NewGuid(), ["Revisão"], Now).Value;
}
