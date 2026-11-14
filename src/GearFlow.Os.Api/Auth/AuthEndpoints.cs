using System.Threading.RateLimiting;
using GearFlow.Os.Api.Jwt;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace GearFlow.Os.Api.Auth;

public sealed record LoginRequest(string? Username, string? Password);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddOsAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        auth.EnsureConsistent();
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName));

        // Atrás do ELB todos os clientes chegam com o IP do balanceador, então na prática o limite é global:
        // bom contra força bruta, e 10 tentativas por minuto sobram para quem está testando.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    /// <summary>Declara o esquema Bearer no OpenAPI para o Scalar mostrar onde colar o token.</summary>
    public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options) =>
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Token obtido em POST /api/auth/token."
            };
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = []
            });
            return Task.CompletedTask;
        });

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (!auth.IsConfigured)
        {
            app.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AuthEndpoints))
                .LogWarning("Auth:Username/Auth:Password não configurados: POST /api/auth/token não foi publicado.");
            return app;
        }

        app.MapPost("/api/auth/token", (LoginRequest body, IOptions<AuthOptions> authOptions, IOptions<JwtOptions> jwt) =>
            {
                var options = authOptions.Value;
                if (!CredentialValidator.IsValid(options, body.Username, body.Password))
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Usuário ou senha inválidos.");

                var token = TokenIssuer.Issue(jwt.Value, options.Username, TimeSpan.FromMinutes(options.TokenMinutes), DateTime.UtcNow);
                return Results.Ok(token);
            })
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithTags("Autenticação")
            .WithSummary("Gera token de acesso")
            .WithDescription("Recebe usuário e senha e devolve um JWT para usar no header Authorization: Bearer <token>.")
            .Produces<IssuedToken>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
