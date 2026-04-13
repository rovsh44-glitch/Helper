#nullable enable
// Minimal API glue in these partials triggers noisy false positives from Roslyn nullability analysis.
// Keep suppressions local to endpoint registration instead of project-wide.
// Minimal API delegate signatures still trigger a narrow set of Roslyn nullability mismatches here.
#pragma warning disable CS8600, CS8619, CS8622

using Helper.Runtime.Core;
using Microsoft.AspNetCore.Routing;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
	private static void MapEvolutionAndGenerationEndpoints(IEndpointRouteBuilder endpoints, ApiRuntimeConfig runtimeConfig)
	{
		MapStrategyAndArchitectureEndpoints(endpoints);
		MapEvolutionAndIndexingEndpoints(endpoints, runtimeConfig);
		MapRagGoalAndResearchEndpoints(endpoints);
		MapWorkspaceEndpoints(endpoints);
		MapGenerationTemplateAndBuildEndpoints(endpoints, runtimeConfig);
	}

	private static OSPlatform ResolveTargetOs(string? raw)
	{
		return raw?.Trim().ToLowerInvariant() switch
		{
			"linux" => OSPlatform.Linux,
			"macos" => OSPlatform.MacOS,
			"osx" => OSPlatform.MacOS,
			_ => OSPlatform.Windows
		};
	}
}

