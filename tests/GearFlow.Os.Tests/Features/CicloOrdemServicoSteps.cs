using FluentAssertions;
using GearFlow.Os.Application.UseCases.AbrirOrdemServico;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Application.UseCases.CadastrarCliente;
using GearFlow.Os.Application.UseCases.CadastrarVeiculo;
using GearFlow.Os.Application.UseCases.ConsultarOrdemServico;
using GearFlow.Os.Application.UseCases.FinalizarOrdemServico;
using GearFlow.Os.Tests.Support;
using Reqnroll;

namespace GearFlow.Os.Tests.Features;

[Binding]
public class CicloOrdemServicoSteps
{
    private readonly FakeDatabase _db = new();
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 18, 18, 0, 0, DateTimeKind.Utc));
    private Guid _vehicleId;
    private Guid _orderId;

    [Given("um cliente com um veículo cadastrado")]
    public async Task DadoClienteEVeiculo()
    {
        var client = await new CadastrarClienteHandler(_db, _db, _clock)
            .Handle(new CadastrarClienteCommand("Ana Souza", "529.982.247-25", "ana@oficina.com", null), CancellationToken.None);
        var vehicle = await new CadastrarVeiculoHandler(_db, _db, _db, _clock)
            .Handle(new CadastrarVeiculoCommand(client.Value.Id, "ABC1D23", "Gol"), CancellationToken.None);
        _vehicleId = vehicle.Value.Id;
    }

    [When(@"a recepcionista abre uma OS com o serviço ""(.*)""")]
    public async Task QuandoAbre(string servico)
    {
        var opened = await new AbrirOrdemServicoHandler(_db, _db, _db, _db, _clock)
            .Handle(new AbrirOrdemServicoCommand(_vehicleId, [servico]), CancellationToken.None);
        opened.IsSuccess.Should().BeTrue();
        _orderId = opened.Value.Id;
    }

    [When("chega o evento OrcamentoGerado")]
    public async Task QuandoOrcamentoGerado() =>
        (await Status().QuandoOrcamentoGerado(Guid.NewGuid(), _orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();

    [When("chega o evento OrcamentoApproved")]
    public async Task QuandoAprovado() =>
        (await Status().QuandoOrcamentoAprovado(Guid.NewGuid(), _orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();

    [When("chega o evento OperationCompleted")]
    public async Task QuandoOperacao() =>
        (await Status().QuandoOperacaoConcluida(Guid.NewGuid(), _orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();

    [When("a recepcionista registra a entrega")]
    public async Task QuandoEntrega() =>
        (await new FinalizarOrdemServicoHandler(_db, _db, _clock).Handle(_orderId, CancellationToken.None)).IsSuccess.Should().BeTrue();

    [Then(@"o status da OS é ""(.*)""")]
    public async Task EntaoStatus(string status)
    {
        var order = await new ConsultarOrdemServicoHandler(_db).PorId(_orderId, CancellationToken.None);
        order.Value.Status.Should().Be(status);
    }

    [Then(@"a consulta devolve o histórico com ""(.*)""")]
    public async Task EntaoMensagem(string mensagem)
    {
        var order = await new ConsultarOrdemServicoHandler(_db).PorId(_orderId, CancellationToken.None);
        order.Value.History.Should().Contain(item => item.Message == mensagem);
    }

    [Then(@"o histórico contém o status ""(.*)""")]
    public async Task EntaoHistorico(string status)
    {
        var order = await new ConsultarOrdemServicoHandler(_db).PorId(_orderId, CancellationToken.None);
        order.Value.History.Should().Contain(item => item.Status == status);
    }

    private AtualizarStatusOsHandler Status() => new(_db, _db, _db, _clock);
}
