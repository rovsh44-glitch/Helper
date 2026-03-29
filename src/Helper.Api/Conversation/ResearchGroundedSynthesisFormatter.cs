using System.Text;
using Helper.Runtime.Core;
using Helper.Runtime.WebResearch;

namespace Helper.Api.Conversation;

internal sealed record ResearchGroundedSynthesisResult(
    string Content,
    bool HasExplicitDisagreement,
    IReadOnlyList<string> Signals);

internal sealed class ResearchGroundedSynthesisFormatter
{
    private readonly IResearchAnswerSynthesizer _synthesizer;
    private readonly ICitationQuotePolicy _citationQuotePolicy;

    public ResearchGroundedSynthesisFormatter()
        : this(new ResearchAnswerSynthesizer(), new CitationQuotePolicy())
    {
    }

    internal ResearchGroundedSynthesisFormatter(
        IResearchAnswerSynthesizer synthesizer,
        ICitationQuotePolicy? citationQuotePolicy = null)
    {
        _synthesizer = synthesizer;
        _citationQuotePolicy = citationQuotePolicy ?? new CitationQuotePolicy();
    }

    public ResearchGroundedSynthesisResult? TryFormat(
        IReadOnlyList<ClaimGrounding> groundedClaims,
        IReadOnlyList<ResearchEvidenceItem>? evidenceItems,
        string? language,
        string? requestPrompt = null)
    {
        var plan = _synthesizer.Build(groundedClaims, evidenceItems);
        if (plan is null)
        {
            return null;
        }

        var profile = ResearchRequestProfileResolver.From(requestPrompt);
        var isRussian = string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase);
        var content = profile.IsDocumentAnalysis
            ? (isRussian
                ? BuildRussianAnalystMode(plan, profile)
                : BuildEnglishAnalystMode(plan, profile))
            : (isRussian
                ? BuildRussian(plan)
                : BuildEnglish(plan));
        return new ResearchGroundedSynthesisResult(
            content,
            plan.Disagreement is not null,
            plan.Disagreement is null
                ? Array.Empty<string>()
                : new[] { $"research_synthesis.disagreement={plan.Disagreement.Kind}" });
    }

    private string BuildEnglishAnalystMode(ResearchAnswerSynthesisPlan plan, ResearchRequestProfile profile)
    {
        var builder = new StringBuilder();
        var first = plan.Frames[0];

        builder.Append("In short, ");
        builder.Append(BuildLead(plan, false));
        builder.AppendLine();

        builder.Append("The main source here is ")
            .Append(first.Title)
            .Append(" [")
            .Append(first.CitationLabel)
            .Append("], and it centers on ")
            .Append(DescribeFocus(first, false))
            .AppendLine(".");

        if (plan.Frames.Count > 1)
        {
            var second = plan.Frames[1];
            builder.Append(second.Title)
                .Append(" [")
                .Append(second.CitationLabel)
                .Append("] adds ")
                .Append(DescribeFocus(second, false))
                .AppendLine(".");
        }

        builder.AppendLine();
        builder.Append("My view: ");
        builder.Append(BuildEnglishAssessment(plan, profile));
        builder.AppendLine();
        builder.Append("Main limitation: ");
        builder.Append(BuildEnglishLimitation(plan, profile));

        return builder.ToString().TrimEnd();
    }

    private string BuildRussianAnalystMode(ResearchAnswerSynthesisPlan plan, ResearchRequestProfile profile)
    {
        var builder = new StringBuilder();
        var first = plan.Frames[0];

        builder.Append("Коротко: ");
        builder.Append(BuildLead(plan, true));
        builder.AppendLine();

        builder.Append("Основной источник здесь ")
            .Append(first.Title)
            .Append(" [")
            .Append(first.CitationLabel)
            .Append("], и он концентрируется на ")
            .Append(DescribeFocus(first, true))
            .AppendLine(".");

        if (plan.Frames.Count > 1)
        {
            var second = plan.Frames[1];
            builder.Append(second.Title)
                .Append(" [")
                .Append(second.CitationLabel)
                .Append("] добавляет ")
                .Append(DescribeFocus(second, true))
                .AppendLine(".");
        }

        builder.AppendLine();
        builder.Append("Моё мнение: ");
        builder.Append(BuildRussianAssessment(plan, profile));
        builder.AppendLine();
        builder.Append("Главное ограничение: ");
        builder.Append(BuildRussianLimitation(plan, profile));

        return builder.ToString().TrimEnd();
    }

    private string BuildEnglish(ResearchAnswerSynthesisPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append("The strongest supported reading is this: ");
        builder.Append(BuildLead(plan, false));
        builder.AppendLine();

        var first = plan.Frames[0];
        if (plan.Frames.Count == 1)
        {
            builder.Append(first.Title)
                .Append(" [")
                .Append(first.CitationLabel)
                .Append("] is the main source here, and it emphasizes ")
                .Append(DescribeFocus(first, false))
                .Append('.');
        }
        else
        {
            var second = plan.Frames[1];
            builder.Append(first.Title)
                .Append(" [")
                .Append(first.CitationLabel)
                .Append("] emphasizes ")
                .Append(DescribeFocus(first, false))
                .Append(", while ")
                .Append(second.Title)
                .Append(" [")
                .Append(second.CitationLabel)
                .Append("] emphasizes ")
                .Append(DescribeFocus(second, false))
                .AppendLine(".");

            if (plan.Frames.Count >= 3)
            {
                var third = plan.Frames[2];
                builder.Append(third.Title)
                    .Append(" [")
                    .Append(third.CitationLabel)
                    .Append("] adds ")
                    .Append(DescribeFocus(third, false))
                    .AppendLine(".");
            }

            if (plan.Disagreement is not null)
            {
                builder.Append("The main disagreement is explicit: ")
                    .Append(plan.Disagreement.Left.Title)
                    .Append(" [")
                    .Append(plan.Disagreement.Left.CitationLabel)
                    .Append("] supports ")
                    .Append(ToSentenceFragment(plan.Disagreement.Left.FocusText))
                    .Append(", whereas ")
                    .Append(plan.Disagreement.Right.Title)
                    .Append(" [")
                    .Append(plan.Disagreement.Right.CitationLabel)
                    .Append("] supports ")
                    .Append(ToSentenceFragment(plan.Disagreement.Right.FocusText))
                    .AppendLine(".");
                builder.Append("So the safest conclusion is to keep the shared framing and treat the conflicting detail as unresolved.");
            }
            else
            {
                builder.Append("Taken together, the sources are complementary rather than redundant.");
            }
        }

        AppendCautionIfNeeded(builder, plan, false);
        return builder.ToString().TrimEnd();
    }

    private string BuildRussian(ResearchAnswerSynthesisPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append("Самое надёжно подтверждённое чтение такое: ");
        builder.Append(BuildLead(plan, true));
        builder.AppendLine();

        var first = plan.Frames[0];
        if (plan.Frames.Count == 1)
        {
            builder.Append(first.Title)
                .Append(" [")
                .Append(first.CitationLabel)
                .Append("] здесь основной источник, и он акцентирует ")
                .Append(DescribeFocus(first, true))
                .Append('.');
        }
        else
        {
            var second = plan.Frames[1];
            builder.Append(first.Title)
                .Append(" [")
                .Append(first.CitationLabel)
                .Append("] акцентирует ")
                .Append(DescribeFocus(first, true))
                .Append(", тогда как ")
                .Append(second.Title)
                .Append(" [")
                .Append(second.CitationLabel)
                .Append("] акцентирует ")
                .Append(DescribeFocus(second, true))
                .AppendLine(".");

            if (plan.Frames.Count >= 3)
            {
                var third = plan.Frames[2];
                builder.Append(third.Title)
                    .Append(" [")
                    .Append(third.CitationLabel)
                    .Append("] добавляет ")
                    .Append(DescribeFocus(third, true))
                    .AppendLine(".");
            }

            if (plan.Disagreement is not null)
            {
                builder.Append("Ключевое расхождение выражено явно: ")
                    .Append(plan.Disagreement.Left.Title)
                    .Append(" [")
                    .Append(plan.Disagreement.Left.CitationLabel)
                    .Append("] поддерживает тезис о том, что ")
                    .Append(ToSentenceFragment(plan.Disagreement.Left.FocusText))
                    .Append(", тогда как ")
                    .Append(plan.Disagreement.Right.Title)
                    .Append(" [")
                    .Append(plan.Disagreement.Right.CitationLabel)
                    .Append("] поддерживает тезис о том, что ")
                    .Append(ToSentenceFragment(plan.Disagreement.Right.FocusText))
                    .AppendLine(".");
                builder.Append("Поэтому безопаснее удерживать общий вывод, а конфликтующую деталь пометить как не до конца согласованную.");
            }
            else
            {
                builder.Append("Вместе эти источники дополняют друг друга, а не повторяют одно и то же.");
            }
        }

        AppendCautionIfNeeded(builder, plan, true);
        return builder.ToString().TrimEnd();
    }

    private static void AppendCautionIfNeeded(StringBuilder builder, ResearchAnswerSynthesisPlan plan, bool isRussian)
    {
        if (plan.Disagreement is not null)
        {
            return;
        }

        if (plan.HasUnsupportedDetails)
        {
            builder.AppendLine();
            builder.Append(isRussian
                ? "Часть второстепенных деталей в черновике не получила прямой опоры в найденных источниках."
                : "Some secondary details in the draft were not directly anchored to the retrieved sources.");
        }
    }

    private string BuildLead(ResearchAnswerSynthesisPlan plan, bool isRussian)
    {
        var first = plan.Frames[0];
        if (plan.Frames.Count == 1)
        {
            return $"{ResolveFocusText(first, isRussian)} [{first.CitationLabel}].";
        }

        var second = plan.Frames[1];
        if (plan.Disagreement is not null)
        {
            return isRussian
                ? $"источники сходятся по общей теме, но расходятся в одной из ключевых деталей [{first.CitationLabel}][{second.CitationLabel}]."
                : $"the sources align on the broad topic but diverge on one key detail [{first.CitationLabel}][{second.CitationLabel}].";
        }

        return isRussian
            ? $"{ResolveFocusText(first, true)} [{first.CitationLabel}], тогда как {ResolveFocusText(second, true)} [{second.CitationLabel}]."
            : $"{ResolveFocusText(first, false)} [{first.CitationLabel}], while {ResolveFocusText(second, false)} [{second.CitationLabel}].";
    }

    private string DescribeFocus(ResearchAnswerFrame frame, bool isRussian)
    {
        if (!frame.FocusDerivedFromEvidence)
        {
            var claim = frame.FocusText;
            return isRussian
                ? $"тезис о том, что {ToSentenceFragment(claim)}"
                : $"the point that {ToSentenceFragment(claim)}";
        }

        var decision = _citationQuotePolicy.Build(
            frame.SupportingExcerpt,
            frame.Evidence.Url,
            frame.Evidence.Title,
            frame.Evidence.EvidenceKind,
            CitationRenderSurface.Answer);
        if (decision.Included && !string.IsNullOrWhiteSpace(decision.Text))
        {
            return isRussian
                ? $"короткий зафиксированный фрагмент: \"{decision.Text}\""
                : $"a short captured excerpt: \"{decision.Text}\"";
        }

        return isRussian
            ? ResolveRussianEvidenceFallback(frame.Evidence.EvidenceKind)
            : ResolveEnglishEvidenceFallback(frame.Evidence.EvidenceKind);
    }

    private string ResolveFocusText(ResearchAnswerFrame frame, bool isRussian)
    {
        if (!frame.FocusDerivedFromEvidence)
        {
            return frame.FocusText;
        }

        var decision = _citationQuotePolicy.Build(
            frame.SupportingExcerpt,
            frame.Evidence.Url,
            frame.Evidence.Title,
            frame.Evidence.EvidenceKind,
            CitationRenderSurface.Answer);
        if (decision.Included && !string.IsNullOrWhiteSpace(decision.Text))
        {
            return decision.DirectQuoteAllowed
                ? $"\"{decision.Text}\""
                : decision.Text;
        }

        return isRussian
            ? ResolveRussianEvidenceFallback(frame.Evidence.EvidenceKind)
            : ResolveEnglishEvidenceFallback(frame.Evidence.EvidenceKind);
    }

    private static string BuildEnglishAssessment(ResearchAnswerSynthesisPlan plan, ResearchRequestProfile profile)
    {
        if (plan.Disagreement is not null)
        {
            return "the topic is interesting, but the retrieved evidence is internally unresolved on one of the key details, so I would treat the strongest claim cautiously.";
        }

        return profile.LooksLikePaperOrArticle
            ? "the idea looks technically interesting and plausibly useful, but I would still want broader replication, stronger ablations, and cross-source confirmation before treating it as a clear architectural upgrade."
            : "the source looks informative, but I would still distinguish the directly supported point from any stronger interpretation that would require a fuller reading.";
    }

    private static string BuildRussianAssessment(ResearchAnswerSynthesisPlan plan, ResearchRequestProfile profile)
    {
        if (plan.Disagreement is not null)
        {
            return "тема выглядит интересной, но найденные данные расходятся в одной из ключевых деталей, поэтому самый сильный тезис здесь стоит трактовать осторожно.";
        }

        return profile.LooksLikePaperOrArticle
            ? "идея выглядит технически интересной и потенциально полезной, но я бы всё равно хотел увидеть более широкую репликацию, более сильные абляции и независимое подтверждение, прежде чем считать её явным архитектурным улучшением."
            : "источник выглядит информативным, но я бы всё равно отделял прямо подтверждённый вывод от более сильной интерпретации, для которой нужен более полный разбор документа.";
    }

    private static string BuildEnglishLimitation(ResearchAnswerSynthesisPlan plan, ResearchRequestProfile profile)
    {
        if (plan.Disagreement is not null)
        {
            return "the sources do not fully agree, so at least one detail remains unresolved.";
        }

        return profile.LooksLikePaperOrArticle
            ? "this answer is grounded in the retrieved document evidence, not in a full independent reproduction or a wider literature comparison."
            : "this answer is grounded in the retrieved source evidence, not in a full direct read of every surrounding source.";
    }

    private static string BuildRussianLimitation(ResearchAnswerSynthesisPlan plan, ResearchRequestProfile profile)
    {
        if (plan.Disagreement is not null)
        {
            return "источники не совпадают полностью, поэтому как минимум одна деталь остаётся не до конца согласованной.";
        }

        return profile.LooksLikePaperOrArticle
            ? "этот ответ опирается на найденные документные фрагменты, а не на полную независимую репликацию или широкий обзор литературы."
            : "этот ответ опирается на найденные фрагменты источника, а не на полный прямой разбор всех смежных материалов.";
    }

    private static string ToSentenceFragment(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return sentence;
        }

        var chars = sentence.ToCharArray();
        if (chars.Length >= 2 && char.IsUpper(chars[0]) && char.IsLower(chars[1]))
        {
            chars[0] = char.ToLowerInvariant(chars[0]);
        }

        return new string(chars);
    }

    private static string ResolveEnglishEvidenceFallback(string? evidenceKind)
    {
        return string.Equals(evidenceKind, "fetched_page", StringComparison.OrdinalIgnoreCase)
            ? "the captured evidence on that page"
            : "the captured source evidence";
    }

    private static string ResolveRussianEvidenceFallback(string? evidenceKind)
    {
        return string.Equals(evidenceKind, "fetched_page", StringComparison.OrdinalIgnoreCase)
            ? "зафиксированное подтверждение на этой странице"
            : "зафиксированное подтверждение из источника";
    }
}

