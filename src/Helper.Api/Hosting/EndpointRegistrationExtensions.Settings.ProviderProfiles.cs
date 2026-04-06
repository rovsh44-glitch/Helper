#nullable enable
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

using Helper.Api.Backend.Providers;
using Microsoft.AspNetCore.Mvc;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private static void MapSettingsProviderProfileEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings/provider-profiles", (Func<IProviderProfileCatalog, IResult>)(catalog =>
        {
            return Results.Ok(ProviderProfileDtoMapper.ToDto(catalog.GetSnapshot()));
        }));

        endpoints.MapGet("/api/settings/provider-profiles/active", (Func<IProviderProfileCatalog, IResult>)(catalog =>
        {
            var active = catalog.GetActiveProfile();
            return active is null
                ? Results.NotFound(new { success = false, error = "No active provider profile is configured." })
                : Results.Ok(ProviderProfileDtoMapper.ToDto(active));
        }));

        endpoints.MapPost("/api/settings/provider-profiles/activate", (Func<ProviderActivationRequestDto?, IProviderProfileActivationService, CancellationToken, Task<IResult>>)(async ([FromBody] ProviderActivationRequestDto? dto, IProviderProfileActivationService activation, CancellationToken ct) =>
        {
            var result = await activation.ActivateAsync(dto?.ProfileId ?? string.Empty, ct);
            return Results.Ok(ProviderProfileDtoMapper.ToDto(result));
        }));

        endpoints.MapPost("/api/settings/provider-profiles/recommend", (Func<ProviderRecommendationRequestDto?, IProviderProfileCatalog, IProviderRecommendationPolicy, IResult>)(([FromBody] ProviderRecommendationRequestDto? dto, IProviderProfileCatalog catalog, IProviderRecommendationPolicy recommendationPolicy) =>
        {
            if (string.IsNullOrWhiteSpace(dto?.Goal))
            {
                return Results.BadRequest(new { success = false, error = "Goal is required." });
            }

            var snapshot = catalog.GetSnapshot();
            var result = recommendationPolicy.Recommend(ProviderProfileDtoMapper.ToDomain(dto), snapshot.Profiles);
            return Results.Ok(ProviderProfileDtoMapper.ToDto(result));
        }));
    }
}
