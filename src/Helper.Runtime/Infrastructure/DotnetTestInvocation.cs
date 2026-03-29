namespace Helper.Runtime.Infrastructure;

internal sealed record DotnetTestInvocation(
    string Target,
    IReadOnlyList<string> BaseArguments,
    string? FilterExpression,
    IReadOnlyList<string> ClassNames,
    bool UseBatchedRunner,
    bool ShowProcessMonitor,
    bool ApplyDefaultArgumentsWhenEmpty,
    int MaxDurationSec,
    string? LogPath,
    string? ErrorLogPath,
    string? StatusPath,
    string? ResultsRoot)
{
    public string BuildCommandDisplay()
    {
        var parts = new List<string> { "dotnet", "test", QuoteIfNeeded(Target) };
        foreach (var argument in GetEffectiveArguments())
        {
            parts.Add(QuoteIfNeeded(argument));
        }

        return string.Join(" ", parts);
    }

    public IReadOnlyList<string> GetEffectiveArguments()
    {
        var arguments = new List<string>();
        arguments.AddRange(GetBaseArgumentsWithDefaults());

        if (!string.IsNullOrWhiteSpace(FilterExpression))
        {
            arguments.Add("--filter");
            arguments.Add(FilterExpression);
        }

        return arguments;
    }

    public IReadOnlyList<string> GetBaseArgumentsWithDefaults()
    {
        var arguments = new List<string>();
        if (ApplyDefaultArgumentsWhenEmpty && BaseArguments.Count == 0)
        {
            arguments.AddRange(UseBatchedRunner
                ? DotnetTestCommandSupport.DefaultBatchedArguments
                : DotnetTestCommandSupport.DefaultSingleRunArguments);
        }

        arguments.AddRange(BaseArguments);
        return arguments;
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        if (value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '|', ';' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
