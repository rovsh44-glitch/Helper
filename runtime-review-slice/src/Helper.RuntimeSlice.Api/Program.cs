using System.Text.Json;
using Helper.RuntimeSlice.Api;
using Helper.RuntimeSlice.Api.Services;

var repoRoot = RuntimeSlicePaths.DiscoverRepoRoot(AppContext.BaseDirectory);
var fixtureRoot = RuntimeSlicePaths.ResolveFixtureRoot(repoRoot);
var webRoot = RuntimeSlicePaths.ResolveWebRoot(repoRoot);

if (args.Contains("--print-openapi", StringComparer.OrdinalIgnoreCase))
{
    var payload = JsonSerializer.Serialize(RuntimeSliceOpenApiDocumentFactory.Create(), RuntimeSliceJson.Options);
    Console.WriteLine(payload);
    return;
}

var builder = webRoot is null
    ? WebApplication.CreateBuilder(args)
    : WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        WebRootPath = webRoot
    });

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.WriteIndented = true;
});

var runtimeOptions = new RuntimeSliceOptions(
    RepoRoot: repoRoot,
    FixtureRoot: fixtureRoot,
    WebRoot: webRoot,
    FixtureMode: true,
    ProductName: "Helper",
    SliceName: "Runtime Review Slice");

builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddSingleton<IRuntimeSliceAboutService, RuntimeSliceAboutService>();
builder.Services.AddSingleton<IRuntimeSliceReadinessService, RuntimeSliceReadinessService>();
builder.Services.AddSingleton<IRuntimeSliceEvolutionService, RuntimeSliceEvolutionService>();
builder.Services.AddSingleton<IRuntimeSliceLibraryService, RuntimeSliceLibraryService>();
builder.Services.AddSingleton<IRuntimeSliceRouteTelemetryService, RuntimeSliceRouteTelemetryService>();
builder.Services.AddSingleton<IRuntimeSliceLogService, RuntimeSliceLogService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("RuntimeSliceLocalUi", policy =>
        policy
            .WithOrigins("http://localhost:4174", "http://127.0.0.1:4174")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors("RuntimeSliceLocalUi");

if (!string.IsNullOrWhiteSpace(app.Environment.WebRootPath) && Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapGet("/api/about", (IRuntimeSliceAboutService about) => Results.Ok(about.GetSnapshot()));
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTimeOffset.UtcNow,
    slice = "runtime-review"
}));
app.MapGet("/api/readiness", (IRuntimeSliceReadinessService readiness) => Results.Ok(readiness.GetSnapshot()));
app.MapGet("/api/openapi.json", () => Results.Ok(RuntimeSliceOpenApiDocumentFactory.Create()));
app.MapGet("/api/runtime/logs", (int? tail, int? maxSources, IRuntimeSliceLogService logs) =>
    Results.Ok(logs.GetSnapshot(tail ?? 60, maxSources ?? 4)));
app.MapGet("/api/evolution/status", (IRuntimeSliceEvolutionService evolution) => Results.Ok(evolution.GetSnapshot()));
app.MapGet("/api/evolution/library", (IRuntimeSliceLibraryService library) => Results.Ok(library.GetSnapshot()));
app.MapGet("/api/telemetry/routes", (IRuntimeSliceRouteTelemetryService telemetry) => Results.Ok(telemetry.GetSnapshot()));

if (!string.IsNullOrWhiteSpace(app.Environment.WebRootPath) && Directory.Exists(app.Environment.WebRootPath))
{
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath!, "index.html"));
    });
}

app.Run();
