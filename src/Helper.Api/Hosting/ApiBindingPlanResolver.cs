using System.Net;
using System.Net.Sockets;

namespace Helper.Api.Hosting;

internal sealed record ApiBindingIntent(int? HelperPort, string? AspNetCoreUrls)
{
    public bool ShouldSuppressAspNetCoreUrls => HelperPort.HasValue && !string.IsNullOrWhiteSpace(AspNetCoreUrls);
}

internal sealed record ApiBindingPlan(
    int PrimaryPort,
    bool ConfigureLoopbackListener,
    bool AllowPortFallback,
    string? ConfiguredUrls,
    string StartupDisplayUrl)
{
    public bool UsesConfiguredUrls => !string.IsNullOrWhiteSpace(ConfiguredUrls);
}

internal static class ApiBindingPlanResolver
{
    public static ApiBindingIntent ReadIntent()
    {
        int? helperPort = null;
        var helperPortText = Environment.GetEnvironmentVariable("HELPER_API_PORT");
        if (int.TryParse(helperPortText, out var parsedHelperPort) && parsedHelperPort is > 0 and < 65536)
        {
            helperPort = parsedHelperPort;
        }

        return new ApiBindingIntent(helperPort, Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
    }

    public static ApiBindingPlan Resolve(ApiBindingIntent intent)
    {
        if (intent.HelperPort.HasValue)
        {
            var helperUrl = $"http://localhost:{intent.HelperPort.Value}";
            return new ApiBindingPlan(intent.HelperPort.Value, ConfigureLoopbackListener: true, AllowPortFallback: false, ConfiguredUrls: null, helperUrl);
        }

        if (TryResolvePreferredUrl(intent.AspNetCoreUrls, out var configuredUrl, out var configuredPort))
        {
            return new ApiBindingPlan(configuredPort, ConfigureLoopbackListener: false, AllowPortFallback: false, ConfiguredUrls: intent.AspNetCoreUrls, configuredUrl);
        }

        return new ApiBindingPlan(5000, ConfigureLoopbackListener: true, AllowPortFallback: true, ConfiguredUrls: null, "http://localhost:5000");
    }

    public static string ResolveStartupDisplayUrl(ApiBindingPlan plan, int activePort)
        => plan.UsesConfiguredUrls ? plan.StartupDisplayUrl : $"http://localhost:{activePort}";

    public static void EnsureConfiguredUrlsAvailable(string urlList)
    {
        var checkedPorts = new HashSet<int>();
        foreach (var uri in EnumerateUrls(urlList))
        {
            if (!ShouldProbe(uri) || !checkedPorts.Add(uri.Port))
            {
                continue;
            }

            try
            {
                EnsureLoopbackPortAvailable(uri.Port);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Configured ASPNETCORE_URLS endpoint '{uri}' is unavailable.", ex);
            }
        }
    }

    public static void EnsureLoopbackPortAvailable(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
    }

    private static bool TryResolvePreferredUrl(string? urlList, out string preferredUrl, out int preferredPort)
    {
        preferredUrl = string.Empty;
        preferredPort = 0;

        var candidates = EnumerateUrls(urlList).ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        var preferred = candidates.FirstOrDefault(static uri => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            ?? candidates[0];

        preferredUrl = preferred.GetLeftPart(UriPartial.Authority);
        preferredPort = preferred.Port;
        return preferredPort > 0;
    }

    private static IEnumerable<Uri> EnumerateUrls(string? urlList)
    {
        if (string.IsNullOrWhiteSpace(urlList))
        {
            yield break;
        }

        foreach (var rawCandidate in urlList.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(rawCandidate, UriKind.Absolute, out var uri) && uri.Port > 0)
            {
                yield return uri;
            }
        }
    }

    private static bool ShouldProbe(Uri uri)
    {
        if (uri.Port <= 0)
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }
}

