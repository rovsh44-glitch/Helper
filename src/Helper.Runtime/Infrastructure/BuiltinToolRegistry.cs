namespace Helper.Runtime.Infrastructure;

internal sealed class BuiltinToolRegistry
{
    private readonly ToolExecutionGateway _executionGateway;

    public BuiltinToolRegistry(ToolExecutionGateway executionGateway)
    {
        _executionGateway = executionGateway;
    }

    public void Register(ToolRegistry registry)
    {
        registry.Register(
            "dotnet_test",
            "Run dotnet test safely with structured class-name filters and optional live process monitor. Prefer this over generic shell execution for dotnet test runs.",
            new Dictionary<string, string>
            {
                { "target", "Required test project or solution path." },
                { "classNames", "Optional array/string of test class names. Helper builds a correct FullyQualifiedName filter automatically." },
                { "filter", "Optional raw dotnet test filter. Used only when classNames are not supplied." },
                { "baseArguments", "Optional extra dotnet test arguments, for example [\"--no-build\", \"-m:1\"]." },
                { "showProcessMonitor", "Optional boolean. Opens a live monitor console window." },
                { "batched", "Optional boolean. Uses class-batched runner instead of a single filtered run." }
            },
            _executionGateway.ExecuteDotnetTestAsync);

        registry.Register(
            "read_file",
            "Read content of a file",
            new Dictionary<string, string> { { "path", "Path to the file" } },
            _executionGateway.ReadFileAsync);

        registry.Register(
            "write_file",
            "Write content to a file",
            new Dictionary<string, string>
            {
                { "path", "Path to the file" },
                { "content", "Content to write" }
            },
            _executionGateway.WriteFileAsync);
    }
}

