#nullable enable
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

using Helper.Api.Backend.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private static void MapSettingsDoctorEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/settings/runtime-doctor/run", (Func<ProviderDoctorRunRequestDto?, IProviderDoctorService, CancellationToken, Task<IResult>>)(async ([FromBody] ProviderDoctorRunRequestDto? dto, IProviderDoctorService doctor, CancellationToken ct) =>
        {
            var report = await doctor.RunAsync(dto?.ProfileId, dto?.IncludeInactive ?? true, ct);
            return Results.Ok(report);
        }));
    }
}
