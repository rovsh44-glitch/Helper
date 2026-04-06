namespace Helper.Runtime.Infrastructure;

internal static class DotnetTimeoutPolicy
{
    private const string GenericTimeoutEnvName = "HELPER_DOTNET_TIMEOUT_SEC";
    private const string RestoreTimeoutEnvName = "HELPER_DOTNET_RESTORE_TIMEOUT_SEC";
    private const string BuildTimeoutEnvName = "HELPER_DOTNET_BUILD_TIMEOUT_SEC";
    private const string TestTimeoutEnvName = "HELPER_DOTNET_TEST_TIMEOUT_SEC";
    private const string HeartbeatEnvName = "HELPER_DOTNET_TRACE_HEARTBEAT_SEC";
    private const string KillConfirmationTimeoutEnvName = "HELPER_DOTNET_KILL_CONFIRM_TIMEOUT_SEC";

    public static TimeSpan ResolveTimeoutBudget(DotnetOperationKind operation)
    {
        var specificEnvName = operation switch
        {
            DotnetOperationKind.Restore => RestoreTimeoutEnvName,
            DotnetOperationKind.Build => BuildTimeoutEnvName,
            DotnetOperationKind.Test => TestTimeoutEnvName,
            _ => GenericTimeoutEnvName
        };
        var fallbackSeconds = operation switch
        {
            DotnetOperationKind.Restore => 90,
            DotnetOperationKind.Build => 180,
            DotnetOperationKind.Test => 240,
            _ => 180
        };

        return TimeSpan.FromSeconds(ReadPositiveInt(specificEnvName, GenericTimeoutEnvName, fallbackSeconds, min: 1, max: 1800));
    }

    public static TimeSpan ResolveHeartbeatInterval()
        => TimeSpan.FromSeconds(ReadPositiveInt(HeartbeatEnvName, fallbackEnvName: null, fallback: 10, min: 1, max: 300));

    public static TimeSpan ResolveKillConfirmationTimeout()
        => TimeSpan.FromSeconds(ReadPositiveInt(KillConfirmationTimeoutEnvName, fallbackEnvName: null, fallback: 15, min: 1, max: 120));

    private static int ReadPositiveInt(string envName, string? fallbackEnvName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(fallbackEnvName))
        {
            raw = Environment.GetEnvironmentVariable(fallbackEnvName);
        }

        if (!int.TryParse(raw, out var parsed))
        {
            return fallback;
        }

        if (parsed < min)
        {
            return min;
        }

        return parsed > max ? max : parsed;
    }
}
