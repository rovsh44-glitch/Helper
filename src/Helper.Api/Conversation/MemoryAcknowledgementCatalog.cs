namespace Helper.Api.Conversation;

internal static class MemoryAcknowledgementCatalog
{
    private static readonly VariationCandidate[] EnglishLeadCandidates =
    {
        new(
            "Understood. I will keep this preference in mind for this conversation",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "neutral", "formal" }),
        new(
            "Understood. I will keep that in mind",
            DetailLevels: new[] { "concise" },
            Formalities: new[] { "formal" },
            PreferShort: true),
        new(
            "Got it. I'll keep that in mind",
            DetailLevels: new[] { "concise" },
            Formalities: new[] { "casual", "neutral" },
            PreferShort: true),
        new(
            "Noted. I will keep that preference active for this conversation",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "formal", "neutral" }),
        new(
            "Okay, I'll carry that preference through this chat",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "casual" }),
        new(
            "Acknowledged. I will apply that preference throughout this conversation",
            DetailLevels: new[] { "deep" },
            Formalities: new[] { "formal" },
            PreferExpanded: true),
        new(
            "All right. I'll keep that preference in place for the rest of this chat",
            DetailLevels: new[] { "deep" },
            Formalities: new[] { "casual", "neutral" },
            PreferExpanded: true)
    };

    private static readonly VariationCandidate[] RussianLeadCandidates =
    {
        new(
            "Понял. Буду учитывать это предпочтение в текущем диалоге",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "neutral", "formal" }),
        new(
            "Принято. Буду это учитывать",
            DetailLevels: new[] { "concise" },
            Formalities: new[] { "formal" },
            PreferShort: true),
        new(
            "Понял. Буду это учитывать",
            DetailLevels: new[] { "concise" },
            Formalities: new[] { "casual", "neutral" },
            PreferShort: true),
        new(
            "Хорошо, буду держать это предпочтение в уме",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "casual" }),
        new(
            "Зафиксировал. Дальше буду опираться на это предпочтение",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "neutral", "formal" }),
        new(
            "Понял. Сохраню это предпочтение для текущего диалога",
            DetailLevels: new[] { "deep" },
            Formalities: new[] { "neutral" },
            PreferExpanded: true),
        new(
            "Принял. Буду держать это предпочтение в контексте разговора",
            DetailLevels: new[] { "deep" },
            Formalities: new[] { "formal" },
            PreferExpanded: true)
    };

    private static readonly VariationCandidate[] EnglishNextStepCandidates =
    {
        new(
            "You can continue; I will apply that preference in this conversation.",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "neutral", "formal" }),
        new(
            "Please continue; I will keep that preference active in this conversation.",
            DetailLevels: new[] { "concise" },
            Formalities: new[] { "formal" },
            PreferShort: true),
        new(
            "Go ahead; I'll keep that preference active.",
            DetailLevels: new[] { "concise" },
            Formalities: new[] { "casual", "neutral" },
            PreferShort: true),
        new(
            "Go on; I'll use that preference in the next answers too.",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "casual" }),
        new(
            "We can continue; I'll carry that preference through the rest of this chat.",
            DetailLevels: new[] { "deep" },
            Formalities: new[] { "casual", "neutral" },
            PreferExpanded: true),
        new(
            "If you continue, I will keep that preference active for the rest of this conversation.",
            DetailLevels: new[] { "deep" },
            Formalities: new[] { "formal" },
            PreferExpanded: true)
    };

    private static readonly VariationCandidate[] RussianNextStepCandidates =
    {
        new(
            "Можете продолжать; я буду учитывать это предпочтение в текущем диалоге.",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "neutral", "formal" }),
        new(
            "Продолжайте, я это учту.",
            DetailLevels: new[] { "concise" },
            Formalities: new[] { "neutral", "formal" },
            PreferShort: true),
        new(
            "Продолжайте, дальше буду учитывать это предпочтение.",
            DetailLevels: new[] { "concise" },
            Formalities: new[] { "casual" },
            PreferShort: true),
        new(
            "Идём дальше; буду учитывать это предпочтение в следующих ответах.",
            DetailLevels: new[] { "balanced" },
            Formalities: new[] { "casual", "neutral" }),
        new(
            "Можем идти дальше; это предпочтение останется в контексте разговора.",
            DetailLevels: new[] { "deep" },
            Formalities: new[] { "neutral" },
            PreferExpanded: true),
        new(
            "Если продолжим, я сохраню это предпочтение на весь текущий диалог.",
            DetailLevels: new[] { "deep" },
            Formalities: new[] { "formal" },
            PreferExpanded: true)
    };

    private static readonly string[] LeadFingerprints = EnglishLeadCandidates
        .Concat(RussianLeadCandidates)
        .Select(candidate => ConversationVariationPolicy.BuildFingerprint(candidate.Text))
        .Where(fingerprint => fingerprint.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static readonly string[] NextStepFingerprints = EnglishNextStepCandidates
        .Concat(RussianNextStepCandidates)
        .Select(candidate => ConversationVariationPolicy.BuildFingerprint(candidate.Text))
        .Where(fingerprint => fingerprint.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static IReadOnlyList<VariationCandidate> GetLeadCandidates(bool isRussian)
    {
        return isRussian ? RussianLeadCandidates : EnglishLeadCandidates;
    }

    public static IReadOnlyList<VariationCandidate> GetNextStepCandidates(bool isRussian)
    {
        return isRussian ? RussianNextStepCandidates : EnglishNextStepCandidates;
    }

    public static string SelectLead(IConversationVariationPolicy policy, ChatTurnContext context, bool isRussian)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var candidates = GetLeadCandidates(isRussian);
        return policy is ConversationVariationPolicy advancedPolicy
            ? advancedPolicy.Select(DialogAct.AckMemory, VariationSlot.MemoryAckLead, context, candidates)
            : policy.Select(DialogAct.AckMemory, VariationSlot.MemoryAckLead, context, candidates.Select(candidate => candidate.Text).ToArray());
    }

    public static string SelectNextStep(IConversationVariationPolicy policy, ChatTurnContext context, bool isRussian)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var candidates = GetNextStepCandidates(isRussian);
        return policy is ConversationVariationPolicy advancedPolicy
            ? advancedPolicy.Select(DialogAct.NextStep, VariationSlot.MemoryAckNextStep, context, candidates)
            : policy.Select(DialogAct.NextStep, VariationSlot.MemoryAckNextStep, context, candidates.Select(candidate => candidate.Text).ToArray());
    }

    public static bool MatchesLeadTemplate(string? responseText)
    {
        var fingerprint = ConversationVariationPolicy.BuildFingerprint(responseText);
        return MatchesFingerprint(fingerprint, LeadFingerprints);
    }

    public static bool MatchesNextStepTemplate(string? nextStep)
    {
        var fingerprint = ConversationVariationPolicy.BuildFingerprint(nextStep);
        return MatchesFingerprint(fingerprint, NextStepFingerprints);
    }

    private static bool MatchesFingerprint(string fingerprint, IReadOnlyList<string> knownFingerprints)
    {
        if (fingerprint.Length == 0)
        {
            return false;
        }

        foreach (var known in knownFingerprints)
        {
            if (fingerprint.StartsWith(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

