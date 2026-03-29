using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

internal interface IAnswerShapePolicy
{
    string ApplyAnswerShapePreference(ChatTurnContext context, string solution, ComposerLocalization localization);
    string ApplyTaskClassFormatting(ChatTurnContext context, string solution, ComposerLocalization localization);
    bool HasStructuredShape(string solution);
}

internal sealed class AnswerShapePolicy : IAnswerShapePolicy
{
    private static readonly Regex ListLineRegex = new(@"(^|\n)\s*(([-*])|(\d+\.))\s+", RegexOptions.Compiled);
    private static readonly Regex ParagraphBreakRegex = new(@"\r?\n\s*\r?\n", RegexOptions.Compiled);
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[\.\!\?])\s+", RegexOptions.Compiled);
    private static readonly string[] ProceduralSignals =
    {
        "step", "steps", "step-by-step", "checklist", "plan", "rollout", "migration", "procedure", "runbook",
        "how to", "walk me through", "implement", "deploy", "fix this", "what should i do",
        "шаг", "шаги", "пошаг", "чек-лист", "чеклист", "план", "роллаут", "миграц", "как сделать", "что делать", "исправь"
    };
    private static readonly string[] ExplicitStructureSignals =
    {
        "bullet", "bullets", "list", "table", "checklist", "step-by-step",
        "список", "пункт", "таблиц", "чек-лист", "чеклист", "пошаг"
    };
    private static readonly string[] ShortAnswerSignals =
    {
        "briefly", "brief", "short", "quickly", "in one sentence", "what is", "why is",
        "кратко", "коротко", "в двух словах", "что такое", "почему"
    };

    public string ApplyAnswerShapePreference(ChatTurnContext context, string solution, ComposerLocalization localization)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return solution;
        }

        if (ResponseComposerSupport.IsOperationalTurn(context) ||
            context.Sources.Count > 0 ||
            ResponseComposerSupport.ContainsSourcesSection(solution))
        {
            return solution;
        }

        var shape = NormalizeAnswerShape(context.Conversation.DefaultAnswerShape);
        return shape switch
        {
            "bullets" when ShouldAllowBulletPreference(context, solution) => TryConvertParagraphToBullets(solution) ?? solution,
            "paragraph" when ShouldAllowParagraphPreference(context, solution) => TryConvertBulletsToParagraph(solution, localization) ?? solution,
            _ => solution
        };
    }

    public string ApplyTaskClassFormatting(ChatTurnContext context, string solution, ComposerLocalization localization)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return solution;
        }

        if (ResponseComposerSupport.IsOperationalTurn(context) ||
            context.Sources.Count > 0 ||
            ResponseComposerSupport.ContainsSourcesSection(solution))
        {
            return solution;
        }

        if (ShouldCollapseUnnecessaryListification(context, solution))
        {
            return TryConvertBulletsToParagraph(solution, localization) ?? solution;
        }

        if (ShouldPromoteToProceduralStructure(context, solution))
        {
            return TryConvertParagraphToStructured(context, solution) ?? solution;
        }

        return solution;
    }

    public bool HasStructuredShape(string solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
        {
            return false;
        }

        return ListLineRegex.IsMatch(solution) ||
               ParagraphBreakRegex.IsMatch(solution);
    }

    private bool ShouldAllowBulletPreference(ChatTurnContext context, string solution)
    {
        if (LooksShortAnswerRequest(context) || LooksDefinitionLikeRequest(context))
        {
            return false;
        }

        if (LooksProceduralRequest(context) || PrefersStructuredShape(context))
        {
            return true;
        }

        return solution.Length >= 180 && ExtractParagraphSentences(solution).Length >= 3;
    }

    private bool ShouldAllowParagraphPreference(ChatTurnContext context, string solution)
    {
        return !LooksProceduralRequest(context) && !PrefersStructuredShape(context) && ListLineRegex.IsMatch(solution);
    }

    private bool ShouldCollapseUnnecessaryListification(ChatTurnContext context, string solution)
    {
        if (!ListLineRegex.IsMatch(solution))
        {
            return false;
        }

        if (LooksProceduralRequest(context) || PrefersStructuredShape(context) || HasExplicitStructureRequest(context))
        {
            return false;
        }

        var listItems = CountListItems(solution);
        if (listItems == 0 || listItems > 3)
        {
            return false;
        }

        return LooksShortAnswerRequest(context) ||
               LooksDefinitionLikeRequest(context) ||
               NormalizeAnswerShape(context.Conversation.DefaultAnswerShape) == "paragraph";
    }

    private bool ShouldPromoteToProceduralStructure(ChatTurnContext context, string solution)
    {
        if (HasStructuredShape(solution))
        {
            return false;
        }

        return LooksProceduralRequest(context) ||
               PrefersStructuredShape(context) ||
               HasExplicitStructureRequest(context);
    }

    private string? TryConvertParagraphToBullets(string solution)
    {
        if (HasStructuredShape(solution))
        {
            return null;
        }

        var sentences = SentenceSplitRegex
            .Split(solution.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal))
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length >= 12)
            .Take(4)
            .ToArray();

        if (sentences.Length < 2 || sentences.Length > 4)
        {
            return null;
        }

        return string.Join(
            Environment.NewLine,
            sentences.Select(static sentence => $"- {sentence.TrimStart('-', '*', ' ')}"));
    }

    private string? TryConvertParagraphToStructured(ChatTurnContext context, string solution)
    {
        if (HasStructuredShape(solution))
        {
            return null;
        }

        var sentences = ExtractParagraphSentences(solution);
        if (sentences.Length < 2 || sentences.Length > 5)
        {
            return null;
        }

        var useChecklist = PrefersChecklist(context) ||
                           string.Equals(NormalizeAnswerShape(context.Conversation.DefaultAnswerShape), "bullets", StringComparison.Ordinal);
        return string.Join(
            Environment.NewLine,
            sentences.Select((sentence, index) =>
                useChecklist
                    ? $"- {sentence.TrimStart('-', '*', ' ')}"
                    : $"{index + 1}. {sentence.TrimStart('-', '*', ' ')}"));
    }

    private string? TryConvertBulletsToParagraph(string solution, ComposerLocalization localization)
    {
        if (!ListLineRegex.IsMatch(solution))
        {
            return null;
        }

        var lines = solution
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim().TrimEnd('.', ';', ':'))
            .Where(line => line.Length > 0)
            .Take(4)
            .ToArray();

        if (lines.Length < 2)
        {
            return null;
        }

        var separator = ReferenceEquals(localization, ComposerLocalization.Russian) ? "; затем " : "; then ";
        return string.Join(separator, lines).TrimEnd('.', ';') + ".";
    }

    private static string NormalizeAnswerShape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "auto";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "paragraph" or "freeform" => "paragraph",
            "bullets" or "bullet" or "list" => "bullets",
            _ => "auto"
        };
    }

    private static string[] ExtractParagraphSentences(string solution)
    {
        return SentenceSplitRegex
            .Split(solution.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal))
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length >= 14)
            .Take(5)
            .ToArray();
    }

    private int CountListItems(string solution)
    {
        return solution
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(line => line.StartsWith("- ", StringComparison.Ordinal) ||
                           line.StartsWith("* ", StringComparison.Ordinal) ||
                           Regex.IsMatch(line, @"^\d+\.\s+"));
    }

    private static bool LooksProceduralRequest(ChatTurnContext context)
    {
        return ProceduralSignals.Any(signal =>
            context.Request.Message.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasExplicitStructureRequest(ChatTurnContext context)
    {
        return ExplicitStructureSignals.Any(signal =>
            context.Request.Message.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksShortAnswerRequest(ChatTurnContext context)
    {
        return ShortAnswerSignals.Any(signal =>
            context.Request.Message.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksDefinitionLikeRequest(ChatTurnContext context)
    {
        var message = context.Request.Message;
        return message.Contains("what is", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("что такое", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("почему", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("why is", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PrefersStructuredShape(ChatTurnContext context)
    {
        return string.Equals(context.Conversation.PreferredStructure, "step_by_step", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(context.Conversation.PreferredStructure, "checklist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PrefersChecklist(ChatTurnContext context)
    {
        return string.Equals(context.Conversation.PreferredStructure, "checklist", StringComparison.OrdinalIgnoreCase);
    }
}

