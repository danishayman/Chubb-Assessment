using Claims.Api.Auth;
using Claims.Api.Data;
using Claims.Api.Features;
using Claims.Api.Infrastructure;
using Claims.Api.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ClaimsDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Claims")));
builder.Services.AddClaimsAuth(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();
    await db.Database.MigrateAsync();
    await Seeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuth();
app.MapClaims();

app.Run();

public partial class Program;
