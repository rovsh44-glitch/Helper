using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

public sealed class DisabledCodeExecutor : ICodeExecutor
{
    public Task<ExecutionResult> ExecuteAsync(string code, string language = "python", CancellationToken ct = default)
    {
        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "unspecified" : language.Trim().ToLowerInvariant();
        var error = $"Code execution is disabled in the production runtime profile for language '{normalizedLanguage}'.";
        return Task.FromResult(new ExecutionResult(false, string.Empty, error, new List<string>()));
    }
}
