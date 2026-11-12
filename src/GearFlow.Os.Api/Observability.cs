using GearFlow.Os.Application.Messaging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GearFlow.Os.Api;

public static class Observability
{
    public static IServiceCollection AddOsObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var otlp = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("gearflow-os-service"))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();
                // Spans do outbox (os.outbox.publish) e do consumidor (os.consume): sem isto o trace da OS quebra no RabbitMQ.
                tracing.AddSource(OsTelemetry.Name);
                if (!string.IsNullOrWhiteSpace(otlp))
                    tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                if (!string.IsNullOrWhiteSpace(otlp))
                    metrics.AddOtlpExporter();
            });
        return services;
    }
}
