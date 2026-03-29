using System.Text;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal static class LocalLibraryGroundingSupport
{
    public static IReadOnlyList<string> BuildSources(IReadOnlyList<KnowledgeChunk> chunks)
    {
        return chunks
            .Select(ResolveSourceReference)
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
                var snippet = string.Join(
                    " ",
                    group.Select(static chunk => TrimExcerpt(chunk.Content))
                        .Where(static excerpt => !string.IsNullOrWhiteSpace(excerpt))
                        .Take(2));
                return new ResearchEvidenceItem(
                    Ordinal: index + 1,
                    Url: sourceReference,
                    Title: ResolveTitle(exemplar, index + 1),
                    Snippet: string.IsNullOrWhiteSpace(snippet)
                        ? "Local library evidence was retrieved but no clean excerpt was preserved."
                        : snippet,
                    TrustLevel: "local_library",
                    EvidenceKind: "local_library_chunk");
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

