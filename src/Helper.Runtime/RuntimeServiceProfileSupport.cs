namespace Helper.Runtime.Infrastructure;

public static class RuntimeServiceProfileSupport
{
    public const string PrototypeRuntimeServicesEnvName = "HELPER_ENABLE_PROTOTYPE_RUNTIME_SERVICES";

    public static bool PrototypeRuntimeServicesEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(PrototypeRuntimeServicesEnvName);
        return bool.TryParse(raw, out var enabled) && enabled;
    }
}
