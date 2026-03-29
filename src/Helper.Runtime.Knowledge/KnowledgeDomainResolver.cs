using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Knowledge;

public sealed class KnowledgeDomainResolver
{
    private readonly AILink _ai;

    public KnowledgeDomainResolver(AILink ai)
    {
        _ai = ai;
    }

    public async Task<string?> ResolveAsync(string content, string? currentDomain, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(currentDomain))
        {
            return Normalize(currentDomain);
        }

        return Normalize(await DetectAsync(content, ct));
    }

    public string Normalize(string? domain)
        => KnowledgeDomainCatalog.Normalize(domain);

    public async Task<string> DetectAsync(string content, CancellationToken ct = default)
    {
        var sample = content.Length > 2500 ? content[..2500] : content;
        var prompt = $@"Identify the best domain for this library document. OPTIONS: math, physics, neuro, chemistry, biology, geology, robotics, computer_science, medicine, philosophy, history, encyclopedias, psychology, linguistics, art_culture, social_sciences, economics, virology, entomology, anatomy, analysis_strategy, russian_lang_lit, english_lang_lit, sci_fi_concepts, mythology_religion, generic. TEXT: {sample} OUTPUT ONLY THE DOMAIN NAME.";
        try
        {
            var response = await _ai.AskAsync(prompt, ct);
            var domain = response.Trim().ToLowerInvariant().Split(' ', '\n', '\r').FirstOrDefault()?.Replace(".", string.Empty, StringComparison.Ordinal) ?? "generic";
            return domain.Length > 40 ? "generic" : domain;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KnowledgeDomainResolver] Domain detection fallback: {ex.Message}");
            return "generic";
        }
    }
}

