using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Helper.Api.Backend.Capabilities;
using Helper.Api.Backend.Configuration;
using Helper.Api.Backend.ControlPlane;
using Helper.Api.Backend.ModelGateway;
using Helper.Api.Backend.Persistence;
using Helper.Api.Hosting;
using Helper.Runtime.Core;
using Microsoft.AspNetCore.RateLimiting;

if (ApiStartupCommands.TryHandleConfigInventoryCommand(args))
{
    return;
}

if (await HumanLikeCommunicationEvalCommand.TryHandleAsync(args, CancellationToken.None))
{
    return;
}

if (await WebResearchParityEvalCommand.TryHandleAsync(args, CancellationToken.None))
{
    return;
}

var discoveredRoot = ApiProgramHelpers.DiscoverHelperRoot(AppContext.BaseDirectory);
ApiProgramHelpers.LoadEnvironmentFileIfPresent(discoveredRoot);
var bootstrapRoot = ApiProgramHelpers.ResolvePath(
    Environment.GetEnvironmentVariable("HELPER_ROOT"),
    discoveredRoot,
    discoveredRoot);
ApiProgramHelpers.LoadEnvironmentFileIfPresent(bootstrapRoot);
var bindingIntent = ApiBindingPlanResolver.ReadIntent();
if (bindingIntent.ShouldSuppressAspNetCoreUrls)
{
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
}

var bootstrapDistRoot = Path.Combine(bootstrapRoot, "dist");
var builder = ApiWebApplicationBuilderFactory.Create(args, bootstrapDistRoot);
ApiWebApplicationBuilderFactory.LogStaticRootSelection(bootstrapDistRoot);

// Avoid the default Windows Event Log provider. On this machine it can throw
// access-denied during host startup and crash the API before Kestrel is ready.
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});
builder.Logging.AddDebug();

var configuredRoot = ReadConfigValue(builder.Configuration, "Helper:RootPath");
if (string.IsNullOrWhiteSpace(configuredRoot) || configuredRoot == ".")
{
    configuredRoot = discoveredRoot;
}

var rootPath = ApiProgramHelpers.ResolvePath(
    Environment.GetEnvironmentVariable("HELPER_ROOT"),
    configuredRoot,
    discoveredRoot);

var dataRoot = ApiProgramHelpers.ResolveDataRoot(
    Environment.GetEnvironmentVariable("HELPER_DATA_ROOT"),
    rootPath);

var projectsRoot = ApiRuntimePathResolver.ResolveUnderRoot(
    Environment.GetEnvironmentVariable("HELPER_PROJECTS_ROOT"),
    ReadConfigValue(builder.Configuration, "Helper:ProjectsRoot"),
    dataRoot,
    "PROJECTS");

var libraryRoot = ApiRuntimePathResolver.ResolveUnderRoot(
    Environment.GetEnvironmentVariable("HELPER_LIBRARY_ROOT"),
    ReadConfigValue(builder.Configuration, "Helper:LibraryRoot"),
    dataRoot,
    "library");

var logsRoot = ApiRuntimePathResolver.ResolveUnderRoot(
    Environment.GetEnvironmentVariable("HELPER_LOGS_ROOT"),
    ReadConfigValue(builder.Configuration, "Helper:LogsRoot"),
    dataRoot,
    "LOG");

var templatesRoot = ApiRuntimePathResolver.ResolveUnderRoot(
    Environment.GetEnvironmentVariable("HELPER_TEMPLATES_ROOT"),
    ReadConfigValue(builder.Configuration, "Helper:TemplatesRoot"),
    dataRoot,
    Path.Combine("library", "forge_templates"));

var apiKey = Environment.GetEnvironmentVariable("HELPER_API_KEY") ?? ReadConfigValue(builder.Configuration, "Helper:ApiKey");
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("API key is missing. Set HELPER_API_KEY environment variable or Helper:ApiKey configuration.");
}

var runtimeConfig = new ApiRuntimeConfig(rootPath, dataRoot, projectsRoot, libraryRoot, logsRoot, templatesRoot, apiKey);
builder.Services.AddSingleton(runtimeConfig);
builder.Services.AddSingleton<IBackendOptionsCatalog, BackendOptionsCatalog>();
builder.Services.AddSingleton<IBackendRuntimePolicyProvider>(sp => (IBackendRuntimePolicyProvider)sp.GetRequiredService<IBackendOptionsCatalog>());
builder.Services.AddSingleton<IBackendConfigValidator, BackendConfigValidator>();
builder.Services.AddSingleton<IModelGatewayTelemetry, ModelGatewayTelemetry>();
builder.Services.AddSingleton<IModelGateway, HelperModelGateway>();
builder.Services.AddSingleton<ICapabilityCatalogService, RuntimeCapabilityCatalogService>();
builder.Services.AddSingleton<IConversationWriteBehindQueue, ConversationWriteBehindQueue>();
builder.Services.AddSingleton<IBackendControlPlane, BackendControlPlane>();
builder.Services.AddSingleton<IRuntimeLogService, RuntimeLogService>();
builder.Services.AddSingleton<IApiSessionTokenService, ApiSessionTokenService>();
builder.Services.AddSingleton<IAuthKeysStore, AuthKeysStore>();
builder.Services.AddSingleton<IApiAuthorizationService, ApiAuthorizationService>();

var bindingPlan = ApiBindingPlanResolver.Resolve(bindingIntent);
int targetPort = bindingPlan.PrimaryPort;

if (bindingPlan.UsesConfiguredUrls)
{
    ApiBindingPlanResolver.EnsureConfiguredUrlsAvailable(bindingPlan.ConfiguredUrls!);
}
else
{
    bool portFound = false;
    while (!portFound && targetPort < 5100)
    {
        try
        {
            EnsurePortAvailable(targetPort);
            portFound = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Startup] Port {targetPort} unavailable: {ex.Message}");
            if (!bindingPlan.AllowPortFallback)
            {
                throw new InvalidOperationException($"Explicit HELPER_API_PORT={targetPort} is unavailable.", ex);
            }

            targetPort++;
        }
    }
}

if (bindingPlan.ConfigureLoopbackListener)
{
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.Listen(IPAddress.Loopback, targetPort);
    });
}

var portFileWrite = ApiPortFileWriter.TryWrite(runtimeConfig, targetPort);
if (!portFileWrite.Succeeded)
{
    Console.WriteLine($"[Startup] Failed to write API_PORT.txt: {portFileWrite.DiagnosticMessage}");
}
else if (portFileWrite.UsedFallback)
{
    Console.WriteLine($"[Startup] API_PORT.txt written to fallback path: {portFileWrite.WrittenPath}");
}

Console.WriteLine($"🚀 API will start on: {ApiBindingPlanResolver.ResolveStartupDisplayUrl(bindingPlan, targetPort)}");

builder.Services.AddHelperApplicationServices(runtimeConfig);
builder.Services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
builder.Services.AddSingleton<IRequestMetricsService, RequestMetricsService>();
builder.Services.AddSingleton<IConversationMetricsService, ConversationMetricsService>();
builder.Services.AddSingleton<IConversationStageMetricsService, ConversationStageMetricsService>();
builder.Services.AddSingleton<IHelpfulnessTelemetryService, HelpfulnessTelemetryService>();
builder.Services.AddSingleton<IHumanLikeConversationDashboardService, HumanLikeConversationDashboardService>();
builder.Services.AddSingleton<IWebResearchTelemetryService, WebResearchTelemetryService>();
builder.Services.AddSingleton<IChatResilienceTelemetryService, ChatResilienceTelemetryService>();
builder.Services.AddSingleton<IIntentTelemetryService, IntentTelemetryService>();
builder.Services.AddSingleton<IFeatureFlags, FeatureFlags>();
builder.Services.AddSingleton<IStartupReadinessService, StartupReadinessService>();
builder.Services.AddSingleton<IStartupTrafficMonitor, StartupTrafficMonitor>();
builder.Services.AddSingleton<IPrometheusMetricsFormatter, PrometheusMetricsFormatter>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCors(opt => opt.AddPolicy("HelperUI", p =>
    p.WithOrigins("http://localhost:3000", "http://localhost:5173", "http://127.0.0.1:3000", "http://127.0.0.1:5173")
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiter");
        logger.LogWarning("Rate limit rejected request from {RemoteIp} to {Path}.",
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            context.HttpContext.Request.Path.Value ?? "/");
        return ValueTask.CompletedTask;
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "global",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 20,
                AutoReplenishment = true
            }));
});
builder.Services.AddHostedService<PrometheusBroadcastService>();
builder.Services.AddHostedService<ModelWarmupService>();
builder.Services.AddHostedService<ConversationPersistenceWorker>();
builder.Services.AddHostedService<Helper.Api.Conversation.PostTurnAuditWorker>();

var app = builder.Build();
var configValidation = app.Services.GetRequiredService<IBackendConfigValidator>().Validate();
var optionsCatalog = app.Services.GetRequiredService<IBackendOptionsCatalog>();
var fatalValidationAlerts = StartupValidationGuards.GetFatalAlerts(optionsCatalog, runtimeConfig);
if (fatalValidationAlerts.Count > 0)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, fatalValidationAlerts));
}

app.Services.GetRequiredService<IStartupReadinessService>().MarkListening();
if (!configValidation.IsValid)
{
    var readiness = app.Services.GetRequiredService<IStartupReadinessService>();
    foreach (var alert in configValidation.Alerts)
    {
        readiness.MarkDegraded(alert, readyForChat: false);
    }
}
app.UseHelperPipeline(runtimeConfig);
app.MapHub<HelperHub>("/hubs/helper");
app.MapHelperEndpoints(runtimeConfig);

if (!string.IsNullOrWhiteSpace(app.Environment.WebRootPath))
{
    var spaEntryFile = Path.Combine(app.Environment.WebRootPath, "index.html");
    if (File.Exists(spaEntryFile))
    {
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(spaEntryFile);
        });
    }
}

app.Run();

static void EnsurePortAvailable(int port) => ApiBindingPlanResolver.EnsureLoopbackPortAvailable(port);

static string? ReadConfigValue(IConfiguration configuration, string key)
{
    return configuration[key];
}

