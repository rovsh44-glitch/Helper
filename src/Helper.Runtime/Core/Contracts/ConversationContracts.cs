using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.WebResearch;

namespace Helper.Runtime.Core
{
    public record ChatMessage(string Id, string Role, string Content, DateTime Timestamp);
    
    public record SystemStatus(
        bool Healthy, 
        string Version, 
        double VramUsageGb, 
        string ActiveModel,
        string Environment = "Helper",
        double SentimentScore = 0.0);

    public record ThoughtNode(
        string Content, 
        double Score, 
        List<ThoughtNode> Children, 
        List<string>? GeneratedFiles = null);

    public record ResearchResult(
        string Topic,
        string Summary, 
        List<string> Sources, 
        List<string> KeyFindings,
        string FullReport,
        DateTime Timestamp,
        string? RawEvidence = null,
        IReadOnlyList<ResearchEvidenceItem>? EvidenceItems = null,
        IReadOnlyList<string>? SearchTrace = null);

    public record ResearchEvidenceItem(
        int Ordinal,
        string Url,
        string Title,
        string Snippet,
        bool IsFallback = false,
        string TrustLevel = "standard",
        bool WasSanitized = false,
        IReadOnlyList<string>? SafetyFlags = null,
        string EvidenceKind = "search_hit",
        string? PublishedAt = null,
        IReadOnlyList<EvidencePassage>? Passages = null);

    public record ExpertConsultationResult(string Answer, string Domain, List<string> Sources);

    public interface IExpertConsultant
    {
        Task<ExpertConsultationResult?> TryConsultExpertAsync(string query, CancellationToken ct = default);
    }
}


