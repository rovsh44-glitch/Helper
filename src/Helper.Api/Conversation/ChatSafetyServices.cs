using Helper.Api.Hosting;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

public sealed record InputRiskScanResult(bool IsBlocked, string? Reason, IReadOnlyList<string> Flags);
public sealed record OutputSafetyResult(bool IsBlocked, string SanitizedOutput, IReadOnlyList<string> Flags);

public interface IInputRiskScanner
{
    Task<InputRiskScanResult> ScanAsync(string message, IReadOnlyList<AttachmentDto>? attachments, CancellationToken ct);
}

public interface IOutputExfiltrationGuard
{
    Task<OutputSafetyResult> ScanAsync(string output, CancellationToken ct);
}

public sealed class InputRiskScanner : IInputRiskScanner
{
    private readonly InputRiskScannerV2 _inner;

    public InputRiskScanner()
        : this(new InputRiskScannerV2())
    {
    }

    internal InputRiskScanner(InputRiskScannerV2 inner)
    {
        _inner = inner;
    }

    public Task<InputRiskScanResult> ScanAsync(string message, IReadOnlyList<AttachmentDto>? attachments, CancellationToken ct)
        => _inner.ScanAsync(message, attachments, ct);
}

public sealed class InputRiskScannerV2 : IInputRiskScanner
{
    private static readonly string[] InstructionOverrideTokens =
    {
        "ignore previous instructions",
        "ignore all previous instructions",
        "disregard previous instructions",
        "bypass policy",
        "disable safety",
        "jailbreak",
        "role:system",
        "act as system",
        "higher priority instruction"
    };

    private static readonly string[] SystemPromptExfilTokens =
    {
        "reveal system prompt",
        "show me hidden prompt",
        "print system prompt",
        "dump prompt",
        "show hidden prompt"
    };

    private static readonly string[] SensitiveDataTokens =
    {
        "print env",
        "system prompt",
        "api key",
        "access token",
        "session token",
        "credentials",
        "secret",
        "password",
        "private key",
        "cookie"
    };

    private static readonly string[] ExfiltrationIntentTokens =
    {
        "exfiltrate",
        "leak",
        "send",
        "upload",
        "post",
        "paste",
        "forward",
        "share"
    };

    private static readonly string[] ExternalTargetTokens =
    {
        "http://",
        "https://",
        "webhook",
        "telegram",
        "discord",
        "slack",
        "remote server",
        "external endpoint"
    };

    private static readonly string[] ToolAbuseTokens =
    {
        "shell_execute",
        "powershell -enc",
        "rm -rf",
        "del /f /s",
        "format c:",
        "disable defender",
        "add-mppreference",
        "reg add hkcu\\software\\microsoft\\windows\\currentversion\\run"
    };

    private readonly double _riskThreshold;

    public InputRiskScannerV2()
    {
        _riskThreshold = Clamp(ReadDouble("HELPER_INPUT_RISK_BLOCK_THRESHOLD", 0.75), 0.3, 0.99);
    }

    public Task<InputRiskScanResult> ScanAsync(string message, IReadOnlyList<AttachmentDto>? attachments, CancellationToken ct)
    {
        var normalized = (message ?? string.Empty).Trim();
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var riskScore = 0.0;

        riskScore += ScoreInstructionHierarchyRisk(normalized, flags);
        riskScore += ScoreDataExfiltrationRisk(normalized, flags);
        riskScore += ScoreToolAbuseRisk(normalized, flags);
        riskScore += ScoreAttachmentRisk(attachments, flags);

        riskScore = Math.Min(1.0, riskScore);
        var blocked = riskScore >= _riskThreshold || flags.Contains("attachments.unsafe_uri");

        if (!blocked && riskScore >= Math.Max(0.5, _riskThreshold - 0.2))
        {
            flags.Add("input.review_required");
        }

        var orderedFlags = flags.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var reason = blocked
            ? $"Prompt rejected by input safety policy. risk_score={riskScore:F2}; flags={JsonSerializer.Serialize(orderedFlags)}"
            : null;

        return Task.FromResult(new InputRiskScanResult(blocked, reason, orderedFlags));
    }

    private static double ScoreInstructionHierarchyRisk(string message, HashSet<string> flags)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return 0;
        }

        var score = 0.0;
        if (InstructionOverrideTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            flags.Add("injection.instruction_override");
            score += 0.45;
        }

        if (SystemPromptExfilTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            flags.Add("injection.system_prompt_exfiltration");
            score += 0.35;
        }

        return score;
    }

    private static double ScoreDataExfiltrationRisk(string message, HashSet<string> flags)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return 0;
        }

        var hasSensitiveDataTarget = SensitiveDataTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
                                     Regex.IsMatch(message, @"\btokens?\b", RegexOptions.IgnoreCase);
        var hasExfilIntent = ExfiltrationIntentTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase));
        var hasExternalTarget = ExternalTargetTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase));
        var hasSystemPromptTarget = message.Contains("system prompt", StringComparison.OrdinalIgnoreCase) ||
                                    message.Contains("hidden prompt", StringComparison.OrdinalIgnoreCase);

        var score = 0.0;
        if (hasSensitiveDataTarget && hasExfilIntent)
        {
            flags.Add("injection.data_exfil_path");
            score += 0.55;
        }

        if (hasSensitiveDataTarget && hasExternalTarget)
        {
            flags.Add("injection.external_exfil_target");
            score += 0.25;
        }

        if (hasSystemPromptTarget && (hasExfilIntent || hasExternalTarget))
        {
            flags.Add("injection.system_prompt_exfil_path");
            score += 0.35;
        }

        if (message.Contains("print env", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("environment variables", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("injection.env_dump_attempt");
            score += 0.25;
        }

        return score;
    }

    private static double ScoreToolAbuseRisk(string message, HashSet<string> flags)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return 0;
        }

        var score = 0.0;
        if (ToolAbuseTokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            flags.Add("injection.tool_abuse_intent");
            score += 0.55;
        }

        if (message.Contains("ignore security", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("without confirmation", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("injection.safety_bypass_intent");
            score += 0.25;
        }

        return score;
    }

    private static double ScoreAttachmentRisk(IReadOnlyList<AttachmentDto>? attachments, HashSet<string> flags)
    {
        if (attachments is not { Count: > 0 })
        {
            return 0;
        }

        var score = 0.0;
        if (attachments.Count > 8)
        {
            flags.Add("attachments.too_many");
            score += 0.15;
        }

        foreach (var item in attachments)
        {
            if (item.SizeBytes > 25 * 1024 * 1024)
            {
                flags.Add("attachments.too_large");
                score += 0.2;
            }

            var uri = item.ReferenceUri ?? string.Empty;
            if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("attachments.unsafe_uri");
                score += 1.0;
            }
        }

        return score;
    }

    private static double ReadDouble(string envName, double fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return double.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}

public sealed class OutputExfiltrationGuard : IOutputExfiltrationGuard
{
    private readonly OutputExfiltrationGuardV2 _inner;

    public OutputExfiltrationGuard()
        : this(new OutputExfiltrationGuardV2())
    {
    }

    internal OutputExfiltrationGuard(OutputExfiltrationGuardV2 inner)
    {
        _inner = inner;
    }

    public Task<OutputSafetyResult> ScanAsync(string output, CancellationToken ct)
        => _inner.ScanAsync(output, ct);
}

public sealed class OutputExfiltrationGuardV2 : IOutputExfiltrationGuard
{
    private static readonly Regex PrivateKeyBlockPattern = new(
        "-----BEGIN [A-Z ]*PRIVATE KEY-----[\\s\\S]+?-----END [A-Z ]*PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BearerTokenPattern = new(
        "(?i)(authorization\\s*:\\s*bearer\\s+)([A-Za-z0-9._~+/=-]{12,})",
        RegexOptions.Compiled);

    private static readonly Regex ApiKeyAssignmentPattern = new(
        @"(?i)\b(api[_-]?key|access[_-]?token|session[_-]?token|secret|password)\s*[:=]\s*(['""]?)([A-Za-z0-9_\-]{8,}|sk-[A-Za-z0-9]{20,}|AIza[0-9A-Za-z_-]{35}|ghp_[A-Za-z0-9]{20,})\2",
        RegexOptions.Compiled);

    private static readonly Regex OpenAiStyleKeyPattern = new(
        "(?<![A-Za-z0-9])(sk-[A-Za-z0-9]{20,})(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    private static readonly Regex GoogleApiKeyPattern = new(
        "(?<![A-Za-z0-9])(AIza[0-9A-Za-z_-]{35})(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    private static readonly Regex JwtPattern = new(
        "(?<![A-Za-z0-9_-])([A-Za-z0-9_-]{20,}\\.[A-Za-z0-9_-]{20,}\\.[A-Za-z0-9_-]{20,})(?![A-Za-z0-9_-])",
        RegexOptions.Compiled);

    public Task<OutputSafetyResult> ScanAsync(string output, CancellationToken ct)
    {
        var value = output ?? string.Empty;
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sanitized = value;

        sanitized = ReplaceWholeMatch(sanitized, PrivateKeyBlockPattern, "exfiltration.private_key_block", flags);
        sanitized = ReplaceCaptureGroup(sanitized, BearerTokenPattern, 2, "exfiltration.bearer_token", flags);
        sanitized = ReplaceCaptureGroup(sanitized, ApiKeyAssignmentPattern, 3, "exfiltration.secret_assignment", flags);
        sanitized = ReplaceCaptureGroup(sanitized, OpenAiStyleKeyPattern, 1, "exfiltration.openai_key", flags);
        sanitized = ReplaceCaptureGroup(sanitized, GoogleApiKeyPattern, 1, "exfiltration.google_api_key", flags);
        sanitized = ReplaceCaptureGroup(sanitized, JwtPattern, 1, "exfiltration.jwt", flags);

        if (flags.Count == 0)
        {
            return Task.FromResult(new OutputSafetyResult(false, value, Array.Empty<string>()));
        }

        var orderedFlags = flags.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.FromResult(new OutputSafetyResult(true, sanitized, orderedFlags));
    }

    private static string ReplaceWholeMatch(string source, Regex pattern, string flag, HashSet<string> flags)
    {
        return pattern.Replace(source, _ =>
        {
            flags.Add(flag);
            return BuildMask(flag);
        });
    }

    private static string ReplaceCaptureGroup(string source, Regex pattern, int captureGroup, string flag, HashSet<string> flags)
    {
        return pattern.Replace(source, match =>
        {
            if (captureGroup >= match.Groups.Count || !match.Groups[captureGroup].Success)
            {
                flags.Add(flag);
                return BuildMask(flag);
            }

            flags.Add(flag);
            var group = match.Groups[captureGroup];
            var relativeIndex = group.Index - match.Index;
            return match.Value.Remove(relativeIndex, group.Length).Insert(relativeIndex, BuildMask(flag));
        });
    }

    private static string BuildMask(string flag)
    {
        return $"[REDACTED:{flag}]";
    }
}

