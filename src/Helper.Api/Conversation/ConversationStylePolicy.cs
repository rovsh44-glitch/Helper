using Helper.Runtime.Core;
using System.Collections.Generic;

namespace Helper.Api.Conversation;

public sealed record ConversationStyleRoute(
    string Mode,
    string TonePreset,
    string PersonaDescriptor,
    string ModeDirective)
{
    public string BuildSystemHint(ConversationUserProfile profile, string? resolvedLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var effectiveLanguage = string.IsNullOrWhiteSpace(resolvedLanguage)
            ? profile.Language
            : resolvedLanguage.Trim().ToLowerInvariant();

        return string.Join(
            " ",
            $"User profile: language={effectiveLanguage}, detail={profile.DetailLevel}, formality={profile.Formality}, domain={profile.DomainFamiliarity}, structure={profile.PreferredStructure}, warmth={profile.Warmth}, enthusiasm={profile.Enthusiasm}, directness={profile.Directness}, answer_shape={profile.DefaultAnswerShape}.",
            $"Style route: mode={Mode}, tone_preset={TonePreset}.",
            $"Persona: {PersonaDescriptor}.",
            $"Mode guidance: {ModeDirective}");
    }
}

public interface IConversationStylePolicy
{
    ConversationStyleRoute Resolve(ConversationUserProfile profile, ChatTurnContext? context = null);
}

public sealed class ConversationStylePolicy : IConversationStylePolicy
{
    private static readonly ConversationStyleRoute ConversationalProfessional = new(
        Mode: "conversational",
        TonePreset: "conversational_professional",
        PersonaDescriptor: "precise, calm, respectful, naturally conversational",
        ModeDirective: "Start directly, stay human and collaborative, avoid cold distance, avoid rigid protocol framing, and keep the answer clear without sounding theatrical.");

    private static readonly ConversationStyleRoute Professional = new(
        Mode: "professional",
        TonePreset: "professional",
        PersonaDescriptor: "precise, calm, respectful, professionally warm",
        ModeDirective: "Keep a professional structure and careful wording, but do not sound legalistic, cold, or over-formal.");

    private static readonly ConversationStyleRoute Operator = new(
        Mode: "operator",
        TonePreset: "operator",
        PersonaDescriptor: "precise, calm, execution-oriented, low-ceremony",
        ModeDirective: "Optimize for operational clarity, concise execution updates, and direct outcomes without bureaucratic framing.");

    public ConversationStyleRoute Resolve(ConversationUserProfile profile, ChatTurnContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (IsOperatorTurn(context))
        {
            return Operator;
        }

        if (string.Equals(profile.Formality, "formal", StringComparison.OrdinalIgnoreCase))
        {
            return BuildProfessionalRoute(profile);
        }

        return BuildConversationalRoute(profile);
    }

    private static ConversationStyleRoute BuildConversationalRoute(ConversationUserProfile profile)
    {
        var tonePreset = profile switch
        {
            { Warmth: "warm", Directness: "direct" } => "conversational_warm_direct",
            { Warmth: "warm" } => "conversational_warm",
            { Directness: "direct" } => "conversational_direct",
            { Enthusiasm: "high" } => "conversational_energetic",
            _ => ConversationalProfessional.TonePreset
        };

        return new ConversationStyleRoute(
            Mode: ConversationalProfessional.Mode,
            TonePreset: tonePreset,
            PersonaDescriptor: BuildPersonaDescriptor("naturally conversational", profile),
            ModeDirective: BuildConversationalDirective(profile));
    }

    private static ConversationStyleRoute BuildProfessionalRoute(ConversationUserProfile profile)
    {
        var tonePreset = profile.Directness == "direct"
            ? "professional_direct"
            : Professional.TonePreset;

        return new ConversationStyleRoute(
            Mode: Professional.Mode,
            TonePreset: tonePreset,
            PersonaDescriptor: BuildPersonaDescriptor("professionally warm", profile),
            ModeDirective: BuildProfessionalDirective(profile));
    }

    private static bool IsOperatorTurn(ChatTurnContext? context)
    {
        if (context is null)
        {
            return false;
        }

        if (context.Intent.Intent == IntentType.Generate)
        {
            return true;
        }

        if (context.ToolCalls.Any(tool => string.Equals(tool, "helper.generate", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return context.ExecutionOutput.StartsWith("Failed to generate project.", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPersonaDescriptor(string baseline, ConversationUserProfile profile)
    {
        var segments = new List<string> { "precise", "calm", "respectful", baseline };

        if (profile.Warmth == "warm")
        {
            segments.Add("noticeably warm without overdoing it");
        }
        else if (profile.Warmth == "cool")
        {
            segments.Add("contained rather than effusive");
        }

        if (profile.Enthusiasm == "high")
        {
            segments.Add("energetic when it helps");
        }
        else if (profile.Enthusiasm == "low")
        {
            segments.Add("deliberately low-drama");
        }

        if (profile.Directness == "direct")
        {
            segments.Add("comfortably direct");
        }
        else if (profile.Directness == "soft")
        {
            segments.Add("gently phrased");
        }

        return string.Join(", ", segments);
    }

    private static string BuildConversationalDirective(ConversationUserProfile profile)
    {
        var segments = new List<string>
        {
            "Start directly, stay human and collaborative, avoid cold distance, avoid rigid protocol framing, and keep the answer clear without sounding theatrical."
        };

        segments.Add(profile.Warmth switch
        {
            "warm" => "Let a little warmth through in transitions and follow-ups.",
            "cool" => "Stay composed and restrained rather than chatty.",
            _ => "Keep the tone balanced rather than flat or over-friendly."
        });
        segments.Add(profile.Enthusiasm switch
        {
            "high" => "Use a bit more visible energy when proposing the next useful step.",
            "low" => "Keep excitement muted and rely on substance over momentum language.",
            _ => "Keep the energy level steady and grounded."
        });
        segments.Add(profile.Directness switch
        {
            "direct" => "Use shorter lead-ins and get to the point quickly.",
            "soft" => "Use gentler transitions and avoid sounding abrupt.",
            _ => "Balance directness with conversational tact."
        });

        return string.Join(" ", segments);
    }

    private static string BuildProfessionalDirective(ConversationUserProfile profile)
    {
        var segments = new List<string>
        {
            "Keep a professional structure and careful wording, but do not sound legalistic, cold, or over-formal."
        };

        if (profile.Directness == "direct")
        {
            segments.Add("Prefer decisive phrasing over long hedging.");
        }

        if (profile.Warmth == "warm")
        {
            segments.Add("Allow moderate interpersonal warmth without becoming casual.");
        }

        if (profile.Enthusiasm == "low")
        {
            segments.Add("Keep the tone measured and understated.");
        }
        else if (profile.Enthusiasm == "high")
        {
            segments.Add("Allow some forward energy while preserving professional restraint.");
        }

        return string.Join(" ", segments);
    }
}

