namespace Helper.Api.Conversation;

public sealed class UserProfileService : IUserProfileService
{
    private readonly IConversationStylePolicy _stylePolicy;

    public UserProfileService(IConversationStylePolicy? stylePolicy = null)
    {
        _stylePolicy = stylePolicy ?? new ConversationStylePolicy();
    }

    public ConversationUserProfile Resolve(ConversationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (state.SyncRoot)
        {
            return new ConversationUserProfile(
                NormalizeLanguage(state.PreferredLanguage),
                NormalizeDetailLevel(state.DetailLevel),
                NormalizeFormality(state.Formality),
                NormalizeDomainFamiliarity(state.DomainFamiliarity),
                NormalizeStructure(state.PreferredStructure),
                NormalizeWarmth(state.Warmth),
                NormalizeEnthusiasm(state.Enthusiasm),
                NormalizeDirectness(state.Directness),
                NormalizeAnswerShape(state.DefaultAnswerShape),
                NormalizeSearchLocalityHint(state.SearchLocalityHint),
                NormalizeDecisionAssertiveness(state.DecisionAssertiveness),
                NormalizeClarificationTolerance(state.ClarificationTolerance),
                NormalizeCitationPreference(state.CitationPreference),
                NormalizeRepairStyle(state.RepairStyle),
                NormalizeReasoningStyle(state.ReasoningStyle),
                NormalizeReasoningEffort(state.ReasoningEffort),
                NormalizePersonaBundleId(state.PersonaBundleId));
        }
    }

    public void ApplyPreferences(ConversationState state, Helper.Api.Hosting.ConversationPreferenceDto dto)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dto);

        lock (state.SyncRoot)
        {
            if (dto.LongTermMemoryEnabled.HasValue)
            {
                state.LongTermMemoryEnabled = dto.LongTermMemoryEnabled.Value;
            }

            if (!string.IsNullOrWhiteSpace(dto.PreferredLanguage))
            {
                state.PreferredLanguage = NormalizeLanguage(dto.PreferredLanguage);
            }

            if (!string.IsNullOrWhiteSpace(dto.DetailLevel))
            {
                state.DetailLevel = NormalizeDetailLevel(dto.DetailLevel);
            }

            if (!string.IsNullOrWhiteSpace(dto.Formality))
            {
                state.Formality = NormalizeFormality(dto.Formality);
            }

            if (!string.IsNullOrWhiteSpace(dto.DomainFamiliarity))
            {
                state.DomainFamiliarity = NormalizeDomainFamiliarity(dto.DomainFamiliarity);
            }

            if (!string.IsNullOrWhiteSpace(dto.PreferredStructure))
            {
                state.PreferredStructure = NormalizeStructure(dto.PreferredStructure);
            }

            if (!string.IsNullOrWhiteSpace(dto.Warmth))
            {
                state.Warmth = NormalizeWarmth(dto.Warmth);
            }

            if (!string.IsNullOrWhiteSpace(dto.Enthusiasm))
            {
                state.Enthusiasm = NormalizeEnthusiasm(dto.Enthusiasm);
            }

            if (!string.IsNullOrWhiteSpace(dto.Directness))
            {
                state.Directness = NormalizeDirectness(dto.Directness);
            }

            if (!string.IsNullOrWhiteSpace(dto.DefaultAnswerShape))
            {
                state.DefaultAnswerShape = NormalizeAnswerShape(dto.DefaultAnswerShape);
            }

            if (dto.SearchLocalityHint is not null)
            {
                state.SearchLocalityHint = NormalizeSearchLocalityHint(dto.SearchLocalityHint);
            }

            if (!string.IsNullOrWhiteSpace(dto.DecisionAssertiveness))
            {
                state.DecisionAssertiveness = NormalizeDecisionAssertiveness(dto.DecisionAssertiveness);
            }

            if (!string.IsNullOrWhiteSpace(dto.ClarificationTolerance))
            {
                state.ClarificationTolerance = NormalizeClarificationTolerance(dto.ClarificationTolerance);
            }

            if (!string.IsNullOrWhiteSpace(dto.CitationPreference))
            {
                state.CitationPreference = NormalizeCitationPreference(dto.CitationPreference);
            }

            if (!string.IsNullOrWhiteSpace(dto.RepairStyle))
            {
                state.RepairStyle = NormalizeRepairStyle(dto.RepairStyle);
            }

            if (!string.IsNullOrWhiteSpace(dto.ReasoningStyle))
            {
                state.ReasoningStyle = NormalizeReasoningStyle(dto.ReasoningStyle);
            }

            if (!string.IsNullOrWhiteSpace(dto.ReasoningEffort))
            {
                state.ReasoningEffort = NormalizeReasoningEffort(dto.ReasoningEffort);
            }

            if (dto.PersonaBundleId is not null)
            {
                state.PersonaBundleId = NormalizePersonaBundleId(dto.PersonaBundleId);
            }

            if (dto.BackgroundResearchEnabled.HasValue)
            {
                state.BackgroundResearchEnabled = dto.BackgroundResearchEnabled.Value;
            }

            if (dto.ProactiveUpdatesEnabled.HasValue)
            {
                state.ProactiveUpdatesEnabled = dto.ProactiveUpdatesEnabled.Value;
            }

            if (!string.IsNullOrWhiteSpace(dto.ProjectId))
            {
                state.ProjectContext = (state.ProjectContext ?? ProjectContextState.Empty(dto.ProjectId)) with
                {
                    ProjectId = dto.ProjectId.Trim(),
                    Label = dto.ProjectLabel is null ? state.ProjectContext?.Label : NormalizeProjectField(dto.ProjectLabel),
                    Instructions = dto.ProjectInstructions is null ? state.ProjectContext?.Instructions : NormalizeProjectField(dto.ProjectInstructions),
                    MemoryEnabled = dto.ProjectMemoryEnabled ?? state.ProjectContext?.MemoryEnabled ?? true,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
            }
            else if ((dto.ProjectLabel is not null || dto.ProjectInstructions is not null || dto.ProjectMemoryEnabled.HasValue) && state.ProjectContext is not null)
            {
                state.ProjectContext = state.ProjectContext with
                {
                    Label = dto.ProjectLabel is null ? state.ProjectContext.Label : NormalizeProjectField(dto.ProjectLabel),
                    Instructions = dto.ProjectInstructions is null ? state.ProjectContext.Instructions : NormalizeProjectField(dto.ProjectInstructions),
                    MemoryEnabled = dto.ProjectMemoryEnabled ?? state.ProjectContext.MemoryEnabled,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
            }

            state.PersonalizationProfile = new PersonalizationProfile(
                ExplanationDepth: state.DetailLevel,
                DecisionAssertiveness: state.DecisionAssertiveness,
                ClarificationTolerance: state.ClarificationTolerance,
                CitationPreference: state.CitationPreference,
                RepairStyle: state.RepairStyle,
                ReasoningStyle: state.ReasoningStyle,
                ReasoningEffort: state.ReasoningEffort,
                PersonaBundleId: state.PersonaBundleId);

            state.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public ConversationStyleRoute ResolveStyleRoute(ConversationUserProfile profile, ChatTurnContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return _stylePolicy.Resolve(profile, context);
    }

    public string BuildSystemHint(ConversationUserProfile profile, ChatTurnContext? context = null, string? resolvedLanguage = null)
    {
        var route = ResolveStyleRoute(profile, context);
        return route.BuildSystemHint(profile, resolvedLanguage ?? context?.ResolvedTurnLanguage);
    }

    private static string NormalizeLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "auto";
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "auto" or "ru" or "en")
        {
            return normalized;
        }

        return normalized switch
        {
            "russian" => "ru",
            "english" => "en",
            _ when normalized.Length == 2 => normalized,
            _ => "auto"
        };
    }

    private static string NormalizeDetailLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "balanced";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "concise" or "short" => "concise",
            "deep" or "detailed" or "long" => "deep",
            _ => "balanced"
        };
    }

    private static string NormalizeFormality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "neutral";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "formal" => "formal",
            "casual" or "informal" => "casual",
            "neutral" or "auto" => "neutral",
            _ => "neutral"
        };
    }

    private static string NormalizeDomainFamiliarity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "intermediate";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "novice" or "beginner" => "novice",
            "intermediate" => "intermediate",
            "expert" or "advanced" => "expert",
            _ => "intermediate"
        };
    }

    private static string NormalizeStructure(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "auto";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => "auto",
            "paragraph" or "freeform" => "paragraph",
            "bullets" or "bullet" or "list" => "bullets",
            "step_by_step" or "step-by-step" or "steps" => "step_by_step",
            "checklist" => "checklist",
            _ => "auto"
        };
    }

    private static string NormalizeWarmth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "balanced";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "cool" or "low" => "cool",
            "warm" or "high" => "warm",
            _ => "balanced"
        };
    }

    private static string NormalizeEnthusiasm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "balanced";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "low" or "calm" => "low",
            "high" or "energetic" => "high",
            _ => "balanced"
        };
    }

    private static string NormalizeDirectness(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "balanced";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "soft" or "gentle" => "soft",
            "direct" or "high" => "direct",
            _ => "balanced"
        };
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

    private static string? NormalizeSearchLocalityHint(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = string.Join(
            ' ',
            value
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length > 80)
        {
            normalized = normalized[..80].TrimEnd();
        }

        return normalized.Equals("near me", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("my location", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("рядом", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static string NormalizeDecisionAssertiveness(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "low" or "cautious" => "low",
            "high" or "decisive" => "high",
            _ => "balanced"
        };
    }

    private static string NormalizeClarificationTolerance(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "low" => "low",
            "high" => "high",
            _ => "balanced"
        };
    }

    private static string NormalizeCitationPreference(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "prefer" or "always" => "prefer",
            "avoid" => "avoid",
            _ => "adaptive"
        };
    }

    private static string NormalizeRepairStyle(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "explain_first" => "explain_first",
            "gentle_reset" => "gentle_reset",
            _ => "direct_fix"
        };
    }

    private static string NormalizeReasoningStyle(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "exploratory" => "exploratory",
            _ => "concise"
        };
    }

    private static string NormalizeReasoningEffort(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "fast" => "fast",
            "deep" => "deep",
            _ => "balanced"
        };
    }

    private static string? NormalizePersonaBundleId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? NormalizeProjectField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

