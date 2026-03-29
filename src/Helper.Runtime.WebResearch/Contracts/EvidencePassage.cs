namespace Helper.Runtime.WebResearch;

public sealed record EvidencePassage(
    string PassageId,
    int EvidenceOrdinal,
    int PassageOrdinal,
    string CitationLabel,
    string Url,
    string Title,
    string? PublishedAt,
    string Text,
    string EvidenceKind = "verified_passage",
    string TrustLevel = "untrusted_web_content",
    bool WasSanitized = false,
    IReadOnlyList<string>? SafetyFlags = null);

