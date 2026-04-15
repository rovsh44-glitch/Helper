using System.Security.Cryptography;
using System.Text;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal static class LocalLibraryGroundingSupport
{
    public static IReadOnlyList<string> BuildSources(IReadOnlyList<KnowledgeChunk> chunks)
    {
        return chunks
            .Select(FormatSourceReferenceForOutput)
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    public static IReadOnlyList<string> BuildTrace(IReadOnlyList<KnowledgeChunk> chunks)
    {
        var sourceCount = BuildSources(chunks).Count;
        var collections = chunks
            .Select(static chunk => chunk.Metadata.GetValueOrDefault("collection", chunk.Collection))
            .Where(static collection => !string.IsNullOrWhiteSpace(collection))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        var channels = chunks
            .Select(static chunk => chunk.Metadata.GetValueOrDefault("retrieval_channel", "vector"))
            .Where(static channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static channel => channel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new[]
        {
            "local_retrieval.mode=hybrid_rrf",
            $"local_retrieval.chunk_count={chunks.Count}",
            $"local_retrieval.source_count={sourceCount}",
            $"local_retrieval.collection_count={collections.Length}",
            $"local_retrieval.channels={(channels.Length == 0 ? "unknown" : string.Join(",", channels))}",
            $"local_retrieval.collections={(collections.Length == 0 ? "none" : string.Join(",", collections))}"
        };
    }

    public static IReadOnlyList<ResearchEvidenceItem> BuildEvidenceItems(IReadOnlyList<KnowledgeChunk> chunks)
    {
        return chunks
            .GroupBy(BuildSourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.ToList())
            .Take(4)
            .Select(static (group, index) =>
            {
                var exemplar = group[0];
                var sourceReference = ResolveSourceReference(exemplar);
                var sourceFormat = ResolveSourceFormat(exemplar);
                var locator = ResolveLocator(exemplar);
                var snippet = string.Join(
                    " ",
                    group.Select(static chunk => TrimExcerpt(chunk.Content))
                        .Where(static excerpt => !string.IsNullOrWhiteSpace(excerpt))
                        .Take(2));
                return new ResearchEvidenceItem(
                    Ordinal: index + 1,
                    Url: FormatSourceReferenceForOutput(exemplar),
                    Title: ResolveTitle(exemplar, index + 1),
                    Snippet: string.IsNullOrWhiteSpace(snippet)
                        ? "Local library evidence was retrieved but no clean excerpt was preserved."
                        : snippet,
                    TrustLevel: "local_library",
                    EvidenceKind: "local_library_chunk",
                    SourceLayer: "local_library",
                    SourceFormat: sourceFormat,
                    SourceId: ResolveSourceId(exemplar, sourceReference),
                    DisplayTitle: ResolveDisplayTitle(exemplar, index + 1),
                    Locator: locator,
                    FreshnessEligibility: ResolveFreshnessEligibility(exemplar),
                    AllowedClaimRoles: ResolveAllowedClaimRoles(exemplar),
                    SourcePath: sourceReference,
                    Collection: ResolveMetadata(exemplar, "collection", exemplar.Collection),
                    IndexedAtUtc: ResolveMetadata(exemplar, "indexed_at_utc"),
                    ContentHash: ResolveMetadata(exemplar, "content_hash", ResolveMetadata(exemplar, "file_hash")),
                    ParserName: ResolveMetadata(exemplar, "parser_name"),
                    ParserVersion: ResolveMetadata(exemplar, "parser_version"),
                    RetrievalScore: ResolveScore(exemplar, "vector_score"),
                    TopicalFitScore: ResolveScore(exemplar, "topical_fit_score"));
            })
            .ToArray();
    }

    public static string BuildPrompt(string topic, IReadOnlyList<KnowledgeChunk> chunks)
    {
        var isRussian = LooksRussianText(topic);
        var builder = new StringBuilder();
        builder.AppendLine(isRussian ? $"Вопрос пользователя: {topic}" : $"User question: {topic}");
        builder.AppendLine(isRussian
            ? "Ниже приведены отрывки из локальной библиотеки пользователя."
            : "Below are excerpts from the user's local library.");
        builder.AppendLine(isRussian
            ? "Отвечай как local-first библиотекарь-исследователь."
            : "Answer as a local-first librarian-researcher.");
        builder.AppendLine(isRussian ? "Требования:" : "Requirements:");
        builder.AppendLine(isRussian ? "- Пиши на русском." : "- Write in the user's language.");
        builder.AppendLine(isRussian
            ? "- Сначала опирайся на локальные отрывки ниже, а не на общие догадки."
            : "- Ground the answer in the local excerpts below rather than in generic guesses.");
        builder.AppendLine(isRussian
            ? "- Используй inline citations вида [1], [2] по локальным источникам."
            : "- Use inline citations like [1], [2] for the local sources.");
        builder.AppendLine(isRussian
            ? "- Явно отделяй то, что поддержано локальной библиотекой, от того, что остаётся непроверенным локально."
            : "- Separate what is supported by the local library from what remains unverified locally.");
        builder.AppendLine(isRussian
            ? "- Не выдумывай web-источники, local library resources вне списка ниже, авторов, URL или свежую проверку."
            : "- Do not invent web sources, local library resources beyond the list below, authors, URLs, or fresh verification.");
        builder.AppendLine(isRussian
            ? "- Заверши фразой, которая начинается с 'Моё мнение:'."
            : "- End with a sentence that starts with 'My view:'.");
        builder.AppendLine();
        builder.AppendLine(isRussian ? "Локальная библиотека:" : "Local library evidence:");

        foreach (var item in BuildEvidenceItems(chunks))
        {
            builder.Append('[').Append(item.Ordinal).Append("] ")
                .Append(item.Title)
                .Append(" | ")
                .AppendLine(item.Url);
            builder.AppendLine(item.Snippet);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildRepairPrompt(
        string topic,
        IReadOnlyList<KnowledgeChunk> chunks,
        string rejectedDraft,
        IReadOnlyList<string> rejectionSignals)
    {
        var isRussian = LooksRussianText(topic);
        var builder = new StringBuilder();
        builder.AppendLine(BuildPrompt(topic, chunks));
        builder.AppendLine();
        builder.AppendLine(isRussian
            ? "Ниже есть неудачный черновик. Перепиши его в короткий grounded local-first ответ."
            : "Below is a failed draft. Rewrite it into a short grounded local-first answer.");
        builder.AppendLine(isRussian
            ? "- Сохрани полезные мысли, если они реально опираются на локальные отрывки."
            : "- Keep useful ideas only if they are grounded in the local excerpts.");
        builder.AppendLine(isRussian
            ? "- Убери шум, выдумки и ссылки на несуществующие источники."
            : "- Remove noise, fabrication, and references to nonexistent sources.");
        if (rejectionSignals.Count > 0)
        {
            builder.AppendLine(isRussian
                ? $"Причины отклонения: {string.Join(", ", rejectionSignals)}."
                : $"Rejection reasons: {string.Join(", ", rejectionSignals)}.");
        }

        builder.AppendLine();
        builder.AppendLine(isRussian ? "Неудачный черновик:" : "Failed draft:");
        builder.AppendLine(rejectedDraft);
        return builder.ToString().TrimEnd();
    }

    public static string BuildDeterministicFallback(string topic, IReadOnlyList<KnowledgeChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return ResearchSynthesisSupport.BuildDeterministicLocalBaselineFallback(topic);
        }

        var evidence = BuildEvidenceItems(chunks);
        var isRussian = LooksRussianText(topic);
        var builder = new StringBuilder();
        if (isRussian)
        {
            builder.Append("По локальной библиотеке тема ");
            builder.Append(BuildTopicAnchor(topic));
            builder.Append(" лучше всего поддерживается следующими опорами: ");
            builder.Append(FormatLocalEvidenceTakeaway(evidence, isRussian));
            builder.AppendLine();
            builder.AppendLine("Этого достаточно для локального базового ответа, но не для утверждений о самых свежих изменениях или внешней проверке.");
            builder.Append("Моё мнение: локальная библиотека уже даёт полезный первый каркас, но всё, что выходит за пределы этих отрывков, нужно проверять отдельно.");
            return builder.ToString().TrimEnd();
        }

        builder.Append("From the local library, ");
        builder.Append(BuildTopicAnchor(topic));
        builder.Append(" is best supported by the following anchors: ");
        builder.Append(FormatLocalEvidenceTakeaway(evidence, isRussian));
        builder.AppendLine();
        builder.AppendLine("That is enough for a local baseline answer, but not for claims about the latest changes or external verification.");
        builder.Append("My view: the local library already gives a useful first-pass frame, but anything beyond those excerpts should be checked separately.");
        return builder.ToString().TrimEnd();
    }

    private static string FormatLocalEvidenceTakeaway(IReadOnlyList<ResearchEvidenceItem> evidence, bool isRussian)
    {
        return string.Join(
            isRussian ? "; " : "; ",
            evidence.Take(2).Select(item => $"[{item.Ordinal}] {item.Title}: {TrimExcerpt(item.Snippet)}"));
    }

    private static string ResolveSourceReference(KnowledgeChunk chunk)
    {
        var source = chunk.Metadata.GetValueOrDefault("source_path")
                     ?? chunk.Metadata.GetValueOrDefault("source_url")
                     ?? chunk.Metadata.GetValueOrDefault("document_id")
                     ?? chunk.Collection;
        return source?.Trim() ?? chunk.Collection;
    }

    private static string FormatSourceReferenceForOutput(KnowledgeChunk chunk)
    {
        if (ShowLocalPaths())
        {
            return ResolveSourceReference(chunk);
        }

        var title = ResolveDisplayTitle(chunk, ordinal: 0);
        var format = ResolveSourceFormat(chunk);
        var locator = ResolveLocator(chunk);
        var sourceId = ResolveSourceId(chunk, ResolveSourceReference(chunk));
        var builder = new StringBuilder();
        builder.Append(title);
        if (!string.IsNullOrWhiteSpace(format))
        {
            builder.Append(" (").Append(format).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(locator))
        {
            builder.Append(" | ").Append(locator);
        }

        builder.Append(" | id=").Append(sourceId);
        return builder.ToString();
    }

    private static string BuildSourceKey(KnowledgeChunk chunk)
    {
        return ResolveSourceReference(chunk);
    }

    private static string ResolveTitle(KnowledgeChunk chunk, int ordinal)
    {
        var title = chunk.Metadata.GetValueOrDefault("title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        var source = ResolveSourceReference(chunk);
        if (!string.IsNullOrWhiteSpace(source))
        {
            var fileName = Path.GetFileName(source);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return $"Local Source {ordinal}";
    }

    private static string ResolveDisplayTitle(KnowledgeChunk chunk, int ordinal)
    {
        return ResolveMetadata(chunk, "display_title") ??
               ResolveMetadata(chunk, "title") ??
               ResolveTitle(chunk, ordinal <= 0 ? 1 : ordinal);
    }

    private static string ResolveSourceFormat(KnowledgeChunk chunk)
    {
        var format = ResolveMetadata(chunk, "source_format") ??
                     ResolveMetadata(chunk, "format") ??
                     Path.GetExtension(ResolveSourceReference(chunk));
        format = (format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(format) ? "unknown" : format;
    }

    private static string ResolveSourceId(KnowledgeChunk chunk, string sourceReference)
    {
        var id = ResolveMetadata(chunk, "source_id") ?? ResolveMetadata(chunk, "document_id");
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id.Trim();
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sourceReference.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private static string ResolveLocator(KnowledgeChunk chunk)
    {
        var existing = ResolveMetadata(chunk, "locator");
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing.Trim();
        }

        var parts = new List<string>();
        var pageStart = ResolveMetadata(chunk, "page_start");
        var pageEnd = ResolveMetadata(chunk, "page_end");
        if (!string.IsNullOrWhiteSpace(pageStart))
        {
            parts.Add(!string.IsNullOrWhiteSpace(pageEnd) && !string.Equals(pageEnd, pageStart, StringComparison.OrdinalIgnoreCase)
                ? $"pages:{pageStart}-{pageEnd}"
                : $"page:{pageStart}");
        }

        var section = ResolveMetadata(chunk, "section_path");
        if (!string.IsNullOrWhiteSpace(section))
        {
            parts.Add($"section:{section}");
        }

        var chunkIndex = ResolveMetadata(chunk, "chunk_index");
        if (!string.IsNullOrWhiteSpace(chunkIndex))
        {
            parts.Add($"chunk:{chunkIndex}");
        }

        return string.Join(" | ", parts);
    }

    private static string ResolveFreshnessEligibility(KnowledgeChunk chunk)
    {
        return ResolveMetadata(chunk, "source_freshness_class") ??
               (string.IsNullOrWhiteSpace(ResolveMetadata(chunk, "published_year"))
                   ? "unknown_date"
                   : "stable_reference");
    }

    private static IReadOnlyList<string> ResolveAllowedClaimRoles(KnowledgeChunk chunk)
    {
        var raw = ResolveMetadata(chunk, "allowed_claim_roles");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new[] { "background", "definition", "methodology", "historical_context", "user_context" };
        }

        return raw
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveMetadata(KnowledgeChunk chunk, string key, string? fallback = null)
    {
        return chunk.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private static double? ResolveScore(KnowledgeChunk chunk, string key)
    {
        var raw = ResolveMetadata(chunk, key);
        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool ShowLocalPaths()
    {
        var explicitPathMode = Environment.GetEnvironmentVariable("HELPER_RESPONSE_SHOW_LOCAL_PATHS");
        if (bool.TryParse(explicitPathMode, out var showPaths) && showPaths)
        {
            return true;
        }

        var publicSourceMode = Environment.GetEnvironmentVariable("HELPER_LOCAL_LIBRARY_PUBLIC_SOURCE_MODE");
        return string.Equals(publicSourceMode, "developer", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimExcerpt(string? content)
    {
        var normalized = (content ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (normalized.Length > 260)
        {
            normalized = normalized[..260].TrimEnd() + "...";
        }

        return normalized;
    }

    private static bool LooksRussianText(string text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.Any(static ch => ch >= '\u0400' && ch <= '\u04FF');
    }

    private static string BuildTopicAnchor(string topic)
    {
        var text = topic.Trim();
        if (text.Length == 0)
        {
            return LooksRussianText(topic) ? "этой теме" : "this topic";
        }

        if (text.Length > 120)
        {
            text = text[..120].TrimEnd();
        }

        text = text.Trim().TrimEnd('.', '!', '?', ':', ';');
        return LooksRussianText(text) ? $"«{text}»" : $"\"{text}\"";
    }
}

