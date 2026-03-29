namespace Helper.Api.Hosting;

public static partial class ServiceRegistrationExtensions
{
    public static IServiceCollection AddHelperApplicationServices(this IServiceCollection services, ApiRuntimeConfig runtimeConfig)
    {
        services
            .AddHelperCoreInfrastructureServices(runtimeConfig)
            .AddHelperGenerationAndTemplateServices(runtimeConfig)
            .AddHelperResearchAndToolingServices()
            .AddHelperOrchestrationServices(runtimeConfig)
            .AddHelperDocumentParserServices()
            .AddHelperConversationServices();

        return services;
    }
}

