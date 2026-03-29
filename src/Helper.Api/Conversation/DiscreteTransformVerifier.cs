using System.Globalization;
using System.Text.RegularExpressions;

namespace Helper.Api.Conversation;

public sealed class DiscreteTransformVerifier : IReasoningOutputVerifier
{
    private static readonly Regex QuotedValuePattern = new("['\"](?<value>[^'\"]+)['\"]", RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new(@"-?\d+", RegexOptions.Compiled);
    private static readonly Dictionary<string, int> RussianWeekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["понедельник"] = 0,
        ["вторник"] = 1,
        ["среда"] = 2,
        ["четверг"] = 3,
        ["пятница"] = 4,
        ["суббота"] = 5,
        ["воскресенье"] = 6
    };

    public int Priority => 20;

    public ValueTask<ReasoningVerifierResult> VerifyAsync(ChatTurnContext context, CancellationToken ct)
    {
        var prompt = context.Request.Message?.Trim() ?? string.Empty;
        var output = NormalizeOutput(context.ExecutionOutput);
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(output))
        {
            return ValueTask.FromResult(new ReasoningVerifierResult(nameof(DiscreteTransformVerifier), ReasoningVerificationStatus.NotApplicable, "Empty prompt or output."));
        }

        if (TryVerifyUppercase(prompt, output, out var uppercaseResult) ||
            TryVerifySequence(prompt, output, out uppercaseResult) ||
            TryVerifySingleLowercaseWord(prompt, output, out uppercaseResult) ||
            TryVerifyRussianDayOfWeek(prompt, output, out uppercaseResult))
        {
            return ValueTask.FromResult(uppercaseResult!);
        }

        return ValueTask.FromResult(new ReasoningVerifierResult(nameof(DiscreteTransformVerifier), ReasoningVerificationStatus.NotApplicable, "No discrete transform rule matched."));
    }

    private static bool TryVerifyUppercase(string prompt, string output, out ReasoningVerifierResult? result)
    {
        result = null;
        if (!prompt.Contains("uppercase", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = QuotedValuePattern.Match(prompt);
        if (!match.Success)
        {
            return false;
        }

        var expected = match.Groups["value"].Value.ToUpperInvariant();
        result = BuildExactMatchResult("uppercase", output, expected);
        return true;
    }

    private static bool TryVerifySequence(string prompt, string output, out ReasoningVerifierResult? result)
    {
        result = null;
        if (!prompt.Contains("next number", StringComparison.OrdinalIgnoreCase) &&
            !prompt.Contains("только числом", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numbers = NumberPattern.Matches(prompt)
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        if (numbers.Length < 3)
        {
            return false;
        }

        int? expected = TryPredictNext(numbers);
        if (!expected.HasValue)
        {
            return false;
        }

        result = BuildExactMatchResult("sequence", output, expected.Value.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private static bool TryVerifySingleLowercaseWord(string prompt, string output, out ReasoningVerifierResult? result)
    {
        result = null;
        if (!prompt.Contains("exactly one lowercase word", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var passed = Regex.IsMatch(output, "^[a-z]+$");
        result = passed
            ? new ReasoningVerifierResult(nameof(DiscreteTransformVerifier), ReasoningVerificationStatus.Approved, "Output satisfied single lowercase word constraint.", null, new[] { "local_verifier:single_word_pass" })
            : new ReasoningVerifierResult(nameof(DiscreteTransformVerifier), ReasoningVerificationStatus.Rejected, "Output must be exactly one lowercase word.", StructuredOutputVerifier.BuildRejectedResponse(output, "Output must be exactly one lowercase word."), new[] { "local_verifier:single_word_fail" });
        return true;
    }

    private static bool TryVerifyRussianDayOfWeek(string prompt, string output, out ReasoningVerifierResult? result)
    {
        result = null;
        if (!prompt.Contains("какой день будет через", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var weekday = RussianWeekdays.Keys.FirstOrDefault(day => prompt.Contains(day, StringComparison.OrdinalIgnoreCase));
        if (weekday is null)
        {
            return false;
        }

        var offsetMatch = NumberPattern.Match(prompt);
        if (!offsetMatch.Success)
        {
            return false;
        }

        var offset = int.Parse(offsetMatch.Value, CultureInfo.InvariantCulture);
        var dayIndex = RussianWeekdays[weekday];
        var expectedIndex = ((dayIndex + offset) % 7 + 7) % 7;
        var expected = RussianWeekdays.First(pair => pair.Value == expectedIndex).Key;
        result = BuildExactMatchResult("weekday", output, expected);
        return true;
    }

    private static ReasoningVerifierResult BuildExactMatchResult(string rule, string output, string expected)
    {
        var normalizedOutput = NormalizeOutput(output);
        var normalizedExpected = NormalizeOutput(expected);
        if (string.Equals(normalizedOutput, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            return new ReasoningVerifierResult(
                nameof(DiscreteTransformVerifier),
                ReasoningVerificationStatus.Approved,
                $"Output passed deterministic {rule} verification.",
                null,
                new[] { $"local_verifier:{rule}_pass" });
        }

        return new ReasoningVerifierResult(
            nameof(DiscreteTransformVerifier),
            ReasoningVerificationStatus.Rejected,
            $"Output failed deterministic {rule} verification. Expected '{expected}'.",
            StructuredOutputVerifier.BuildRejectedResponse(output, $"Expected '{expected}'."),
            new[] { $"local_verifier:{rule}_fail" });
    }

    private static int? TryPredictNext(IReadOnlyList<int> values)
    {
        if (values.Count < 3)
        {
            return null;
        }

        var diffs = values.Skip(1).Zip(values, (current, previous) => current - previous).ToArray();
        if (diffs.All(diff => diff == diffs[0]))
        {
            return values[^1] + diffs[0];
        }

        if (values.Skip(1).All(value => value != 0))
        {
            var ratios = values.Skip(1).Zip(values, (current, previous) => previous == 0 ? double.NaN : current / (double)previous).ToArray();
            if (ratios.All(ratio => !double.IsNaN(ratio)) && ratios.All(ratio => Math.Abs(ratio - ratios[0]) < 0.0001))
            {
                return (int)Math.Round(values[^1] * ratios[0], MidpointRounding.AwayFromZero);
            }
        }

        if (diffs.Length >= 2)
        {
            var secondDiffs = diffs.Skip(1).Zip(diffs, (current, previous) => current - previous).ToArray();
            if (secondDiffs.All(diff => diff == secondDiffs[0]))
            {
                return values[^1] + diffs[^1] + secondDiffs[0];
            }
        }

        return null;
    }

    private static string NormalizeOutput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().TrimEnd('.', '!', '?', ';', ':', '"', '\'').Trim();
    }
}

