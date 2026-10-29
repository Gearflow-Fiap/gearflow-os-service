using GearFlow.Os.Application.UseCases.AbrirOrdemServico;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Application.UseCases.CadastrarCliente;
using GearFlow.Os.Application.UseCases.CadastrarVeiculo;
using GearFlow.Os.Application.UseCases.ConsultarCadastro;
using GearFlow.Os.Application.UseCases.ConsultarOrdemServico;
using GearFlow.Os.Application.UseCases.FinalizarOrdemServico;

namespace GearFlow.Os.Api;

public static class Endpoints
{
    public static IEndpointRouteBuilder MapOsEndpoints(this IEndpointRouteBuilder app)
    {
        var clientes = app.MapGroup("/api/clientes").WithTags("Clientes");
        clientes.MapPost("/", async (CreateClientRequest body, CadastrarClienteHandler handler, CancellationToken ct) =>
            HttpResults.From(
                await handler.Handle(new CadastrarClienteCommand(body.Name, body.Document, body.Email, body.Phone), ct),
                client => Results.Created($"/api/clientes/{client.Id}", client)));
        clientes.MapGet("/{id:guid}", async (Guid id, ConsultarCadastroHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.Cliente(id, ct), Results.Ok));

        var veiculos = app.MapGroup("/api/veiculos").WithTags("Veículos");
        veiculos.MapPost("/", async (CreateVehicleRequest body, CadastrarVeiculoHandler handler, CancellationToken ct) =>
            HttpResults.From(
                await handler.Handle(new CadastrarVeiculoCommand(body.ClientId, body.Plate, body.Model), ct),
                vehicle => Results.Created($"/api/veiculos/{vehicle.Id}", vehicle)));
        veiculos.MapGet("/{id:guid}", async (Guid id, ConsultarCadastroHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.Veiculo(id, ct), Results.Ok));

        var ordens = app.MapGroup("/api/ordens-servico").WithTags("Ordens de serviço");
        ordens.MapPost("/", async (OpenServiceOrderRequest body, AbrirOrdemServicoHandler handler, CancellationToken ct) =>
            HttpResults.From(
                await handler.Handle(new AbrirOrdemServicoCommand(body.VehicleId, body.Services ?? []), ct),
                order => Results.Created($"/api/ordens-servico/{order.Id}", order)));
        ordens.MapGet("/", async (int? page, int? pageSize, bool? incluirEncerradas, ConsultarOrdemServicoHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.Listar(page ?? 1, pageSize ?? 20, incluirEncerradas ?? false, ct)));
        ordens.MapGet("/{id:guid}", async (Guid id, ConsultarOrdemServicoHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.PorId(id, ct), Results.Ok));
        ordens.MapPost("/{id:guid}/entrega", async (Guid id, FinalizarOrdemServicoHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.Handle(id, ct)));

        return app;
    }

    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/eventos", async (DevEventRequest body, AtualizarStatusOsHandler handler, CancellationToken ct) =>
        {
            var messageId = Guid.NewGuid();
            var result = body.Tipo switch
            {
                "OrcamentoGerado" => await handler.QuandoOrcamentoGerado(messageId, body.OrdemServicoId, ct),
                "OrcamentoApproved" => await handler.QuandoOrcamentoAprovado(messageId, body.OrdemServicoId, ct),
                "OrcamentoRejected" => await handler.QuandoOrcamentoRejeitado(messageId, body.OrdemServicoId, ct),
                "OperationCompleted" => await handler.QuandoOperacaoConcluida(messageId, body.OrdemServicoId, ct),
                _ => null
            };

            if (result is null)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Tipo de evento desconhecido.");
            return HttpResults.From(result);
        }).WithTags("Desenvolvimento");

        return app;
    }
}

public sealed record CreateClientRequest(string Name, string Document, string Email, string? Phone);
public sealed record CreateVehicleRequest(Guid ClientId, string Plate, string Model);
public sealed record OpenServiceOrderRequest(Guid VehicleId, IReadOnlyList<string>? Services);
public sealed record DevEventRequest(string Tipo, Guid OrdemServicoId);
