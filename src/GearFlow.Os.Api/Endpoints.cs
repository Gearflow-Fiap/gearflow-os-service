using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GearFlow.Os.Api.Jwt;
using GearFlow.Os.Application.Messaging;
using GearFlow.Os.Application.UseCases.AbrirOrdemServico;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Application.UseCases.CadastrarCliente;
using GearFlow.Os.Application.UseCases.CadastrarVeiculo;
using GearFlow.Os.Application.UseCases.CancelarOrdemServico;
using GearFlow.Os.Application.UseCases.ConsultarCadastro;
using GearFlow.Os.Application.UseCases.ConsultarOrdemServico;
using GearFlow.Os.Application.UseCases.FinalizarOrdemServico;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GearFlow.Os.Api;

public static class Endpoints
{
    public static IEndpointRouteBuilder MapOsEndpoints(this IEndpointRouteBuilder app)
    {
        var clientes = app.MapGroup("/api/clientes").WithTags("Clientes").RequireAuthorization();
        clientes.MapPost("/", async (CreateClientRequest body, CadastrarClienteHandler handler, CancellationToken ct) =>
            HttpResults.From(
                await handler.Handle(new CadastrarClienteCommand(body.Name, body.Document, body.Email, body.Phone), ct),
                client => Results.Created($"/api/clientes/{client.Id}", client)))
            .WithSummary("Cadastra cliente").WithDescription("Cria um cliente com CPF/CNPJ e e-mail.");
        clientes.MapGet("/{id:guid}", async (Guid id, ConsultarCadastroHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.Cliente(id, ct), Results.Ok))
            .WithSummary("Consulta cliente").WithDescription("Retorna os dados do cliente.");

        var veiculos = app.MapGroup("/api/veiculos").WithTags("Veículos").RequireAuthorization();
        veiculos.MapPost("/", async (CreateVehicleRequest body, CadastrarVeiculoHandler handler, CancellationToken ct) =>
            HttpResults.From(
                await handler.Handle(new CadastrarVeiculoCommand(body.ClientId, body.Plate, body.Model), ct),
                vehicle => Results.Created($"/api/veiculos/{vehicle.Id}", vehicle)))
            .WithSummary("Cadastra veículo").WithDescription("Associa um veículo a um cliente.");
        veiculos.MapGet("/{id:guid}", async (Guid id, ConsultarCadastroHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.Veiculo(id, ct), Results.Ok))
            .WithSummary("Consulta veículo").WithDescription("Retorna os dados do veículo.");

        var ordens = app.MapGroup("/api/ordens-servico").WithTags("Ordens de serviço").RequireAuthorization();
        ordens.MapPost("/", async (OpenServiceOrderRequest body, AbrirOrdemServicoHandler handler, CancellationToken ct) =>
            HttpResults.From(
                await handler.Handle(new AbrirOrdemServicoCommand(body.VehicleId, body.Services ?? []), ct),
                order => Results.Created($"/api/ordens-servico/{order.Id}", order)))
            .WithSummary("Abre ordem de serviço")
            .WithDescription("Abre a OS e dispara a Saga (os.created, os.order-created.v1 e StartDiagnostic).");
        ordens.MapGet("/", async (int? page, int? pageSize, bool? incluirEncerradas, ConsultarOrdemServicoHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.Listar(page ?? 1, pageSize ?? 20, incluirEncerradas ?? false, ct)))
            .WithSummary("Lista ordens de serviço").WithDescription("Lista paginada das OS.");
        ordens.MapGet("/{id:guid}", async (Guid id, ConsultarOrdemServicoHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.PorId(id, ct), Results.Ok))
            .WithSummary("Consulta ordem de serviço").WithDescription("Retorna status e histórico da OS.");
        ordens.MapPost("/{id:guid}/entrega", async (Guid id, FinalizarOrdemServicoHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.Handle(id, ct)))
            .WithSummary("Entrega o veículo").WithDescription("Conclui a OS após execução e baixa de estoque.");
        ordens.MapPost("/{id:guid}/cancelamento", async (Guid id, CancelServiceOrderRequest? body, CancelarOrdemServicoHandler handler, CancellationToken ct) =>
            HttpResults.From(await handler.Handle(id, body?.Motivo, ct)))
            .WithSummary("Cancela a ordem de serviço")
            .WithDescription("Cancela a OS antes do fim da execução e dispara as compensações da Saga (CancelOperation, ReleaseStock, RefundPayment ou CancelBudget).");

        return app;
    }

    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/token", (IOptions<JwtOptions> jwt) =>
        {
            var settings = jwt.Value;
            var token = new JwtSecurityToken(
                settings.Issuer,
                settings.Audience,
                [
                    new Claim(JwtRegisteredClaimNames.Sub, "os-dev"),
                    new Claim("actor_type", "staff")
                ],
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret)),
                    SecurityAlgorithms.HmacSha256));
            return Results.Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token) });
        }).AllowAnonymous().WithTags("Desenvolvimento")
            .WithSummary("Emite JWT de desenvolvimento").WithDescription("Só existe em Development.");

        app.MapPost("/api/dev/eventos", async (DevEventRequest body, AtualizarStatusOsHandler handler, CancellationToken ct) =>
        {
            var type = body.Tipo switch
            {
                "OrcamentoGerado" or "BudgetGenerated" => SagaEventTypes.BudgetGenerated,
                "OrcamentoApproved" or "BudgetApproved" => SagaEventTypes.BudgetApproved,
                "OrcamentoRejected" or "BudgetRejected" => SagaEventTypes.BudgetRejected,
                "DiagnosticFinished" => SagaEventTypes.DiagnosticFinished,
                "StockReserved" => SagaEventTypes.StockReserved,
                "StockInsufficient" => SagaEventTypes.StockInsufficient,
                "StockConsumed" => SagaEventTypes.StockConsumed,
                "PaymentConfirmed" => SagaEventTypes.PaymentConfirmed,
                "PaymentFailed" => SagaEventTypes.PaymentFailed,
                "DiagnosticFailed" => SagaEventTypes.DiagnosticFailed,
                "BudgetGenerationFailed" => SagaEventTypes.BudgetGenerationFailed,
                "OperationCanceled" => SagaEventTypes.OperationCanceled,
                "OperationCompleted" or "ExecutionFinished" => SagaEventTypes.ExecutionFinished,
                _ => null
            };

            if (type is null)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Tipo de evento desconhecido.");

            return HttpResults.From(await handler.Handle(
                new IncomingSagaMessage(type, Guid.NewGuid(), body.OrdemServicoId, body.BudgetId, body.PaymentId, null, null, body.Payload ?? "{}"),
                ct));
        }).AllowAnonymous().WithTags("Desenvolvimento")
            .WithSummary("Injeta evento da Saga").WithDescription("Simula Billing, Operation ou Inventory em Development.");

        return app;
    }
}

public sealed record CreateClientRequest(string Name, string Document, string Email, string? Phone);
public sealed record CreateVehicleRequest(Guid ClientId, string Plate, string Model);
public sealed record OpenServiceOrderRequest(Guid VehicleId, IReadOnlyList<string>? Services);
public sealed record CancelServiceOrderRequest(string? Motivo);
public sealed record DevEventRequest(string Tipo, Guid OrdemServicoId, Guid? BudgetId, Guid? PaymentId, string? Payload);
