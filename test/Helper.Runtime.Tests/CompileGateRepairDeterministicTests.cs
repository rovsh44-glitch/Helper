using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Tests;

public sealed class CompileGateRepairDeterministicTests
{
    [Fact]
    public async Task TryApplyRepairsAsync_FixesCs0538_InvalidExplicitInterfaceSpecifier()
    {
        using var temp = new TempDirectoryScope("helper_compile_repair_test_");
        var filePath = Path.Combine(temp.Path, "Worker.cs");
        await File.WriteAllTextAsync(filePath, """
namespace Demo;

public class Worker
{
    public void Worker.InvalidExplicit()
    {
    }
}
""");

        var errors = new[]
        {
            new BuildError("Worker.cs", 5, "CS0538", "'Worker' in explicit interface declaration is not an interface")
        };

        var changed = await CreateService().TryApplyRepairsAsync(temp.Path, errors);
        var updated = await File.ReadAllTextAsync(filePath);

        Assert.True(changed);
        Assert.DoesNotContain("Worker.InvalidExplicit", updated, StringComparison.Ordinal);
        Assert.Contains("InvalidExplicit", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryApplyRepairsAsync_FixesCs1656_MethodGroupAssignment()
    {
        using var temp = new TempDirectoryScope("helper_compile_repair_test_");
        var filePath = Path.Combine(temp.Path, "Worker.cs");
        await File.WriteAllTextAsync(filePath, """
namespace Demo;

public class Worker
{
    private void Execute()
    {
    }

    public void Run()
    {
        Execute = () => { };
    }
}
""");

        var errors = new[]
        {
            new BuildError("Worker.cs", 10, "CS1656", "Cannot assign to 'Execute' because it is a 'method group'")
        };

        var changed = await CreateService().TryApplyRepairsAsync(temp.Path, errors);
        var updated = await File.ReadAllTextAsync(filePath);

        Assert.True(changed);
        Assert.DoesNotContain("Execute =", updated, StringComparison.Ordinal);
        Assert.Contains("Execute();", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryApplyRepairsAsync_FixesCs5001_ByAddingDeterministicEntryPoint()
    {
        using var temp = new TempDirectoryScope("helper_compile_repair_test_");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "Worker.cs"), """
namespace Demo;

public sealed class Worker
{
    public void Run()
    {
    }
}
""");

        var errors = new[]
        {
            new BuildError("GeneratedCompileGate.csproj", 1, "CS5001", "Program does not contain a static 'Main' method suitable for an entry point")
        };

        var changed = await CreateService().TryApplyRepairsAsync(temp.Path, errors);
        var entryPointPath = Path.Combine(temp.Path, "CompileGateEntryPoint.g.cs");

        Assert.True(changed);
        Assert.True(File.Exists(entryPointPath));
        var content = await File.ReadAllTextAsync(entryPointPath);
        Assert.Contains("public static void Main(string[] args)", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryApplyRepairsAsync_FixesCs1983_ByQualifyingTaskReturnType()
    {
        using var temp = new TempDirectoryScope("helper_compile_repair_test_");
        var filePath = Path.Combine(temp.Path, "Worker.cs");
        await File.WriteAllTextAsync(filePath, """
namespace Demo;

using System.Collections.Generic;

public sealed class Task
{
}

public sealed class Worker
{
    public async Task DoAsync()
    {
        return;
    }

    public async Task<List<Task>> GetAsync()
    {
        return default!;
    }
}
""");

        var errors = new[]
        {
            new BuildError("Worker.cs", 11, "CS1983", "The return type of an async method must be void, Task, Task<T>, a task-like type, IAsyncEnumerable<T>, or IAsyncEnumerator<T>")
        };

        var changed = await CreateService().TryApplyRepairsAsync(temp.Path, errors);
        var updated = await File.ReadAllTextAsync(filePath);
        var compact = updated
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        Assert.True(changed);
        Assert.Contains("asyncglobal::System.Threading.Tasks.TaskDoAsync()", compact, StringComparison.Ordinal);
        Assert.Contains("asyncglobal::System.Threading.Tasks.Task<List<Task>>GetAsync()", compact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryApplyRepairsAsync_MissingReturnFix_OnlyTouchesDiagnosticMethod()
    {
        using var temp = new TempDirectoryScope("helper_compile_repair_test_");
        var filePath = Path.Combine(temp.Path, "Calculator.cs");
        var original = """
namespace Demo;

public class Calculator
{
    public int Evaluate(int x)
    {
        if (x > 0)
        {
            return x;
        }
    }

    public int Safe(int x)
    {
        return x + 1;
    }
}
""";
        await File.WriteAllTextAsync(filePath, original);

        var changed = await CreateService().TryApplyRepairsAsync(
            temp.Path,
            new[]
            {
                new BuildError("Calculator.cs", 5, "CS0161", "'Calculator.Evaluate(int)': not all code paths return a value")
            });

        var updated = await File.ReadAllTextAsync(filePath);

        Assert.True(changed);
        Assert.Contains("public int Safe(int x)", updated, StringComparison.Ordinal);
        Assert.Contains("return x + 1;", updated, StringComparison.Ordinal);
        Assert.NotEqual(original, updated);
    }

    [Fact]
    public async Task TryApplyRepairsAsync_UnknownSymbolFallback_OnlyTouchesDiagnosticMethod()
    {
        using var temp = new TempDirectoryScope("helper_compile_repair_test_");
        var filePath = Path.Combine(temp.Path, "Worker.cs");
        var original = """
namespace Demo;

public class Worker
{
    public int Broken()
    {
        return MissingSymbol + 1;
    }

    public int Healthy()
    {
        return 42;
    }
}
""";
        await File.WriteAllTextAsync(filePath, original);

        var changed = await CreateService().TryApplyRepairsAsync(
            temp.Path,
            new[]
            {
                new BuildError("Worker.cs", 7, "CS0103", "The name 'MissingSymbol' does not exist in the current context")
            });

        var updated = await File.ReadAllTextAsync(filePath);

        Assert.True(changed);
        Assert.Contains("public int Healthy()", updated, StringComparison.Ordinal);
        Assert.Contains("return 42;", updated, StringComparison.Ordinal);
        Assert.NotEqual(original, updated);
    }

    [Fact]
    public async Task TryApplyRepairsAsync_TrailingNarrativeFix_OnlyTouchesReportedFile()
    {
        using var temp = new TempDirectoryScope("helper_compile_repair_test_");
        var targetPath = Path.Combine(temp.Path, "Target.cs");
        var untouchedPath = Path.Combine(temp.Path, "Untouched.cs");
        const string narrativeTail = "\nExplanation: this file was generated by an assistant.\n";

        await File.WriteAllTextAsync(
            targetPath,
            """
namespace Demo;

public class Target
{
}
""" + narrativeTail);

        var untouchedOriginal =
            """
namespace Demo;

public class Untouched
{
}
""" + narrativeTail;
        await File.WriteAllTextAsync(untouchedPath, untouchedOriginal);

        var changed = await CreateService().TryApplyRepairsAsync(
            temp.Path,
            new[]
            {
                new BuildError("Target.cs", 7, "CS1002", "; expected")
            });

        var targetUpdated = await File.ReadAllTextAsync(targetPath);
        var untouchedUpdated = await File.ReadAllTextAsync(untouchedPath);

        Assert.True(changed);
        Assert.DoesNotContain("Explanation:", targetUpdated, StringComparison.Ordinal);
        Assert.Equal(untouchedOriginal, untouchedUpdated);
    }

    private static CompileGateRepairService CreateService()
    {
        return new CompileGateRepairService(
            new UsingInferenceService(new TypeTokenExtractor()),
            new MethodBodySemanticGuard());
    }

}

