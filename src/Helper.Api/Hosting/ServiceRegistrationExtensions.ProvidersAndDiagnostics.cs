using Helper.Api.Backend.Diagnostics;
using Helper.Api.Backend.Providers;

namespace Helper.Api.Hosting;

public static partial class ServiceRegistrationExtensions
{
    private static IServiceCollection AddHelperProviderAndDiagnosticsServices(this IServiceCollection services)
    {
        services.AddSingleton<IProviderProfileStore, ProviderProfileStore>();
        services.AddSingleton<IProviderProfileValidator, ProviderProfileValidator>();
        services.AddSingleton<IProviderCapabilityMatrix, ProviderCapabilityMatrix>();
        services.AddSingleton<IProviderProfileCatalog, ProviderProfileCatalog>();
        services.AddSingleton<IProviderRecommendationPolicy, ProviderRecommendationPolicy>();
        services.AddSingleton<IProviderProfileResolver, ProviderProfileResolver>();
        services.AddSingleton<IProviderProfileActivationService, ProviderProfileActivationService>();
        services.AddSingleton<IProviderProbe, OllamaProviderProbe>();
        services.AddSingleton<IProviderProbe, OpenAiCompatibleProviderProbe>();
        services.AddSingleton<IProviderProbeFactory, ProviderProbeFactory>();
        services.AddSingleton<IProviderDoctorService, ProviderDoctorService>();

        return services;
    }
}
