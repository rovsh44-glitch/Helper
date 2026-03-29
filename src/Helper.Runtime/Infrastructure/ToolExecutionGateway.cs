using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure;

internal sealed class ToolExecutionGateway
{
    private readonly IProcessGuard _processGuard;
    private readonly IGoalManager _goalManager;
    private readonly IFileSystemGuard _fileGuard;
    private readonly ISafetyGuard? _safetyGuard;
    private readonly IToolAuditService? _audit;

    public ToolExecutionGateway(
        IProcessGuard processGuard,
        IGoalManager goalManager,
        IFileSystemGuard fileGuard,
        ISafetyGuard? safetyGuard,
        IToolAuditService? audit)
    {
        _processGuard = processGuard;
        _goalManager = goalManager;
        _fileGuard = fileGuard;
        _safetyGuard = safetyGuard;
        _audit = audit;
    }

    public async Task<ToolExecutionResult> ExecuteDotnetTestAsync(Dictionary<string, object> args, CancellationToken ct)
    {
        if (!DotnetTestCommandSupport.TryCreateInvocationFromToolArguments(args, out var invocation, out var parseError))
        {
            return new ToolExecutionResult(false, string.Empty, parseError);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var command = invocation.BuildCommandDisplay();
        var guardError = await TryEnsureSafeCommandAsync(command, HelperWorkspacePathResolver.ResolveHelperRoot(), startedAt, "dotnet_test", ct).ConfigureAwait(false);
        if (guardError is not null)
        {
            return guardError;
        }

        var runner = new DotnetTestToolRunner();
        var result = await runner.ExecuteAsync(invocation, ct).ConfigureAwait(false);
        RecordAudit(startedAt, "dotnet_test", "EXECUTE", result.Success, result.Success ? null : result.Error, command);
        return result;
    }

    public async Task<ToolExecutionResult> ExecuteShellCommandAsync(Dictionary<string, object> args, CancellationToken ct)
    {
        var command = args.GetValueOrDefault("command")?.ToString();
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ToolExecutionResult(false, string.Empty, "Command is missing");
        }

        var workingDir = args.GetValueOrDefault("workingDir")?.ToString() ?? HelperWorkspacePathResolver.ResolveHelperRoot();
        var startedAt = DateTimeOffset.UtcNow;
        if (DotnetTestCommandSupport.IsDotnetTestCommand(command))
        {
            return await ExecuteTranslatedDotnetTestAsync(command, workingDir, startedAt, ct).ConfigureAwait(false);
        }

        var guardError = await TryEnsureSafeCommandAsync(command, workingDir, startedAt, "shell_execute", ct).ConfigureAwait(false);
        if (guardError is not null)
        {
            return guardError;
        }

        var executor = new ShellExecutor();
        var result = await executor.ExecuteSequenceAsync(workingDir, new List<string> { command }).ConfigureAwait(false);
        RecordAudit(startedAt, "shell_execute", "EXECUTE", result.Success, result.Success ? null : result.Output, command);
        return new ToolExecutionResult(result.Success, result.Output);
    }

    public async Task<ToolExecutionResult> ReadFileAsync(Dictionary<string, object> args, CancellationToken ct)
    {
        var path = args.GetValueOrDefault("path")?.ToString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ToolExecutionResult(false, string.Empty, "Path is missing");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var guardError = TryEnsureSafePath(path, startedAt, "read_file", "READ");
        if (guardError is not null)
        {
            return guardError;
        }

        if (!File.Exists(path))
        {
            RecordAudit(startedAt, "read_file", "READ", success: false, "File not found", path);
            return new ToolExecutionResult(false, string.Empty, $"File not found: {path}");
        }

        var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        RecordAudit(startedAt, "read_file", "READ", success: true, null, path);
        return new ToolExecutionResult(true, content);
    }

    public async Task<ToolExecutionResult> WriteFileAsync(Dictionary<string, object> args, CancellationToken ct)
    {
        var path = args.GetValueOrDefault("path")?.ToString();
        var content = args.GetValueOrDefault("content")?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ToolExecutionResult(false, string.Empty, "Path is missing");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var guardError = TryEnsureSafePath(path, startedAt, "write_file", "WRITE");
        if (guardError is not null)
        {
            return guardError;
        }

        if (_safetyGuard is not null)
        {
            var safe = await _safetyGuard.ValidateOperationAsync("WRITE", path, content).ConfigureAwait(false);
            if (!safe)
            {
                RecordAudit(startedAt, "write_file", "WRITE", success: false, "Safety guard rejected write operation.", path);
                return new ToolExecutionResult(false, string.Empty, "Safety guard rejected write operation.");
            }
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
        RecordAudit(startedAt, "write_file", "WRITE", success: true, null, path);
        return new ToolExecutionResult(true, "File written successfully");
    }

    private async Task<ToolExecutionResult> ExecuteTranslatedDotnetTestAsync(
        string command,
        string workingDir,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        if (!DotnetTestCommandSupport.TryParseShellCommand(command, out var translatedInvocation, out var translatedError))
        {
            RecordAudit(startedAt, "shell_execute", "EXECUTE", success: false, translatedError, command);
            return new ToolExecutionResult(false, string.Empty, translatedError);
        }

        if (!Path.IsPathRooted(translatedInvocation.Target))
        {
            translatedInvocation = translatedInvocation with
            {
                Target = Path.GetFullPath(Path.Combine(workingDir, translatedInvocation.Target))
            };
        }

        var translatedCommand = translatedInvocation.BuildCommandDisplay();
        var guardError = await TryEnsureSafeCommandAsync(translatedCommand, workingDir, startedAt, "shell_execute", ct).ConfigureAwait(false);
        if (guardError is not null)
        {
            return guardError;
        }

        var runner = new DotnetTestToolRunner();
        var translatedResult = await runner.ExecuteAsync(translatedInvocation, ct).ConfigureAwait(false);
        RecordAudit(startedAt, "shell_execute", "EXECUTE", translatedResult.Success, translatedResult.Success ? null : translatedResult.Error, translatedCommand);
        return translatedResult;
    }

    private async Task<ToolExecutionResult?> TryEnsureSafeCommandAsync(
        string command,
        string workingDir,
        DateTimeOffset startedAt,
        string toolName,
        CancellationToken ct)
    {
        try
        {
            var activeGoals = await _goalManager.GetActiveGoalsAsync(ct).ConfigureAwait(false);
            await _processGuard.EnsureSafeCommandAsync(command, workingDir, activeGoals, ct).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            RecordAudit(startedAt, toolName, "EXECUTE", success: false, ex.Message, command);
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }

    private ToolExecutionResult? TryEnsureSafePath(
        string path,
        DateTimeOffset startedAt,
        string toolName,
        string operation)
    {
        try
        {
            _fileGuard.EnsureSafePath(path);
            return null;
        }
        catch (Exception ex)
        {
            RecordAudit(startedAt, toolName, operation, success: false, ex.Message, path);
            return new ToolExecutionResult(false, string.Empty, ex.Message);
        }
    }

    private void RecordAudit(
        DateTimeOffset startedAt,
        string toolName,
        string operation,
        bool success,
        string? error,
        string? details)
    {
        _audit?.Record(new ToolAuditEntry(
            startedAt,
            toolName,
            operation,
            success,
            error,
            details,
            "tool_service"));
    }
}

