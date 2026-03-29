using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed record TemplatePromotionOutcome(
    bool Attempted,
    bool Success,
    string TemplateId,
    string? Version,
    string Message,
    IReadOnlyList<string> Errors);

public interface IGenerationTemplatePromotionService
{
    Task<TemplatePromotionOutcome> TryPromoteAsync(
        GenerationRequest request,
        GenerationResult result,
        Action<string>? onProgress = null,
        CancellationToken ct = default);
}

