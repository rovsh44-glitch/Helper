using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Infrastructure;

if (args.Length == 0)
{
    Console.WriteLine("Usage: helper [command] [query]");
    return;
}

var command = args[0].ToLowerInvariant();
var commandArgs = args.Skip(1).ToArray();
var query = string.Join(" ", commandArgs);
var helperRoot = HelperWorkspacePathResolver.ResolveHelperRoot();
var runtime = await HelperCliRuntimeBuilder.BuildAsync(helperRoot, CancellationToken.None);

if (args.Contains("--mcp"))
{
    await runtime.McpServer.RunAsync(CancellationToken.None);
    return;
}

using var cts = new CancellationTokenSource();

try
{
    if (await HelperCliCommandDispatcher.TryHandleAsync(command, commandArgs, query, runtime, cts.Token))
    {
        return;
    }

    Console.WriteLine($"Unknown command: {command}");
    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    Console.WriteLine($"[Error] {ex.Message}");
    Environment.ExitCode = 1;
}

