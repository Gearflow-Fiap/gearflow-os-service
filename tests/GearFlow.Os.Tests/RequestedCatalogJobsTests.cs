using System.Text.Json;
using FluentAssertions;
using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Application.Saga;
using GearFlow.Os.Domain.Customers;
using GearFlow.Os.Domain.Orders;
using GearFlow.Os.Tests.Support;

namespace GearFlow.Os.Tests;

public class RequestedCatalogJobsTests
{
    [Fact]
    public void Comando_preserva_identidades_e_linhas_distintas_do_mesmo_catalogo()
    {
        var catalogId = Guid.NewGuid();
        var lines = new[] { new RequestedService(Guid.NewGuid(), "Revisão", catalogId), new RequestedService(Guid.NewGuid(), "Revisão", catalogId) };
        var order = ServiceOrder.OpenCatalogRequest(Guid.NewGuid(), Guid.NewGuid(), lines, DateTime.UtcNow).Value;
        var outbox = new CapturingOutbox();
        var vehicle = Vehicle.Register(order.ClientId, "ABC1D23", "Modelo", DateTime.UtcNow).Value;
        new SagaOrchestrator(outbox, new FixedClock(DateTime.UtcNow)).OnOpened(order, vehicle);
        var outgoing = outbox.Messages.Single(message => message.Queue == SagaTopology.StartDiagnosticQueue);
        var envelope = (MassTransitEnvelope)outgoing.Payload;
        var command = (StartDiagnostic)envelope.Message;
        command.RequestedJobs.Select(job => job.LineId).Should().Equal(lines.Select(line => line.Id));
        command.RequestedJobs.Should().OnlyContain(job => job.CatalogJobId == catalogId);
        command.ServiceOrderCode.Should().BeNull();
        command.MessageId.Should().Be(outgoing.MessageId).And.Be(envelope.MessageId);
        command.CorrelationId.Should().Be(order.Id).And.Be(envelope.CorrelationId);
        envelope.MessageType.Should().ContainSingle().Which.Should().Be("urn:message:GearFlow.Contracts.Operation.V1:StartDiagnostic");
        var json = JsonSerializer.Serialize(envelope, OsJson.Options);
        JsonDocument.Parse(json).RootElement.GetProperty("message").GetProperty("requestedJobs").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Catalogo_vazio_ou_limite_excedido_nao_abre_ordem()
    {
        var invalid = new[] { new RequestedService(Guid.NewGuid(), "Revisão", Guid.Empty) };
        ServiceOrder.OpenCatalogRequest(Guid.NewGuid(), Guid.NewGuid(), invalid, DateTime.UtcNow).IsFailure.Should().BeTrue();
        var excessive = Enumerable.Range(0, 201).Select(_ => new RequestedService(Guid.NewGuid(), "Revisão", Guid.NewGuid())).ToArray();
        ServiceOrder.OpenCatalogRequest(Guid.NewGuid(), Guid.NewGuid(), excessive, DateTime.UtcNow).IsFailure.Should().BeTrue();
    }

    private sealed class CapturingOutbox : IOutbox
    {
        public List<OutgoingMessage> Messages { get; } = [];
        public void Enqueue(string routingKey, object payload) => Messages.Add(new() { RoutingKey = routingKey, Payload = payload });
        public void Enqueue(OutgoingMessage message) => Messages.Add(message);
    }
}
