using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace Helper.Api.Hosting;

public static class ApplicationPipelineExtensions
{
    public static WebApplication UseHelperPipeline(this WebApplication app, ApiRuntimeConfig runtimeConfig)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var feature = context.Features.Get<IExceptionHandlerFeature>();
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HelperPipeline");
                var correlationId = context.Items.TryGetValue("CorrelationId", out var cid) ? cid?.ToString() : null;
                var canceledByClient = context.RequestAborted.IsCancellationRequested;
                var isCanceled = feature?.Error is OperationCanceledException || canceledByClient;

                if (feature?.Error != null)
                {
                    if (isCanceled)
                    {
                        logger.LogWarning("Request canceled. CorrelationId={CorrelationId}", correlationId);
                    }
                    else
                    {
                        logger.LogError(feature.Error, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);
                    }
                }

                context.Response.StatusCode = isCanceled
                    ? (canceledByClient ? StatusCodes.Status499ClientClosedRequest : StatusCodes.Status408RequestTimeout)
                    : StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                var payload = JsonSerializer.Serialize(new
                {
                    success = false,
                    error = isCanceled ? (canceledByClient ? "Request canceled." : "Request timed out.") : "Internal server error.",
                    correlationId
                });
                await context.Response.WriteAsync(payload);
            });
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseCors("HelperUI");
        app.UseRateLimiter();

        app.Use(async (context, next) =>
        {
            var correlationId = context.Request.Headers.TryGetValue("X-Correlation-ID", out var existing)
                && !string.IsNullOrWhiteSpace(existing)
                    ? existing.ToString()
                    : Guid.NewGuid().ToString("N");

            context.Items["CorrelationId"] = correlationId;
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            await next();
        });

        app.Use(async (context, next) =>
        {
            var metrics = context.RequestServices.GetRequiredService<IRequestMetricsService>();
            var start = DateTimeOffset.UtcNow;
            var statusCode = StatusCodes.Status500InternalServerError;
            try
            {
                await next();
                statusCode = context.Response.StatusCode;
            }
            finally
            {
                var elapsed = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                var canceled = context.RequestAborted.IsCancellationRequested || statusCode == 499;
                metrics.Record(context.Request.Path.Value ?? "/", statusCode, elapsed, canceled);
            }
        });

        app.Use(async (context, next) =>
        {
            var traffic = context.RequestServices.GetRequiredService<IStartupTrafficMonitor>();
            using var trafficScope = traffic.BeginRequest(context.Request.Path.Value ?? string.Empty);

            if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            var requiresAuth =
                path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/hubs/helper", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/hubs/helper/", StringComparison.OrdinalIgnoreCase);

            var isPublicPath =
                path.Equals("/api/auth/session", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/readiness", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/smoke", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/smoke/long", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/smoke/stream", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/smoke/stream/long", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/handshake", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/api/openapi.json", StringComparison.OrdinalIgnoreCase);

            if (requiresAuth && !isPublicPath)
            {
                var authz = context.RequestServices.GetRequiredService<IApiAuthorizationService>();
                var extractedKey = ApiProgramHelpers.ExtractApiKey(context);
                if (!authz.TryAuthorize(extractedKey, out var principal))
                {
                    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth");
                    logger.LogWarning("Unauthorized request to {Path} from {RemoteIp}. CorrelationId={CorrelationId}",
                        path,
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        context.Items.TryGetValue("CorrelationId", out var cid) ? cid?.ToString() : null);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized");
                    return;
                }

                context.Items[ApiAuthorizationService.PrincipalContextKey] = principal;

                var requiredScope = authz.ResolveRequiredScope(path, context.Request.Method);
                if (!string.IsNullOrWhiteSpace(requiredScope))
                {
                    var deny = authz.EnsureScope(context, requiredScope);
                    if (deny != null)
                    {
                        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth");
                        logger.LogWarning("Forbidden request to {Path}. MissingScope={Scope}. CorrelationId={CorrelationId}",
                            path,
                            requiredScope,
                            context.Items.TryGetValue("CorrelationId", out var cid) ? cid?.ToString() : null);
                        await deny.ExecuteAsync(context);
                        return;
                    }
                }
            }

            await next();
        });

        return app;
    }
}

