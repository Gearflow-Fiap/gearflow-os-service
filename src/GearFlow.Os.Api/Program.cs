using GearFlow.Os.Api;
using GearFlow.Os.Domain.Primitives;
using GearFlow.Os.Infrastructure;
using GearFlow.Os.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddOsService(builder.Configuration);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapOsEndpoints();
if (app.Environment.IsDevelopment())
    app.MapDevEndpoints();

app.Run();

static class HttpResults
{
    public static IResult From<T>(Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess)
            return onSuccess(result.Value);
        return Problem(result.Error);
    }

    public static IResult From(Result result)
    {
        if (result.IsSuccess)
            return Results.NoContent();
        return Problem(result.Error);
    }

    private static IResult Problem(Error error) => error.Type switch
    {
        ErrorType.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: error.Description, type: error.Code),
        ErrorType.Conflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: error.Description, type: error.Code),
        ErrorType.Validation => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: error.Description, type: error.Code),
        _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: error.Description, type: error.Code)
    };
}
