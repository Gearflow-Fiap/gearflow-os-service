using GearFlow.Os.Application;
using GearFlow.Os.Application.Abstractions;
using GearFlow.Os.Application.UseCases.AbrirOrdemServico;
using GearFlow.Os.Application.UseCases.AtualizarStatusOs;
using GearFlow.Os.Application.UseCases.CadastrarCliente;
using GearFlow.Os.Application.UseCases.CadastrarVeiculo;
using GearFlow.Os.Application.UseCases.ConsultarCadastro;
using GearFlow.Os.Application.UseCases.ConsultarOrdemServico;
using GearFlow.Os.Application.UseCases.FinalizarOrdemServico;
using GearFlow.Os.Infrastructure.Messaging;
using GearFlow.Os.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GearFlow.Os.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOsService(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OsDb")
            ?? throw new InvalidOperationException("ConnectionStrings:OsDb não configurada.");

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddDbContext<OsDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddScoped<IInbox, EfInbox>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IServiceOrderRepository, ServiceOrderRepository>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<CadastrarClienteHandler>();
        services.AddScoped<CadastrarVeiculoHandler>();
        services.AddScoped<AbrirOrdemServicoHandler>();
        services.AddScoped<AtualizarStatusOsHandler>();
        services.AddScoped<FinalizarOrdemServicoHandler>();
        services.AddScoped<ConsultarOrdemServicoHandler>();
        services.AddScoped<ConsultarCadastroHandler>();

        services.AddHostedService<OutboxDispatcher>();
        services.AddHostedService<ServiceOrderConsumer>();
        return services;
    }
}
