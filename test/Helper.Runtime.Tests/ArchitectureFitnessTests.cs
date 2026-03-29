namespace Helper.Runtime.Tests;

public partial class ArchitectureFitnessTests
{
    private static string ResolveWorkspaceFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "Helper.sln");
            if (File.Exists(marker))
            {
                return Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root was not found.");
    }

    private static int ReadIntBudget(params string[] segments)
    {
        return ReadBudgetNode(segments).GetInt32();
    }

    private static string[] ReadStringArrayBudget(params string[] segments)
    {
        return ReadBudgetNode(segments)
            .EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static bool TryReadStringArrayBudget(out string[] values, params string[] segments)
    {
        var budgetPath = ResolveWorkspaceFile("scripts", "performance_budgets.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(budgetPath));
        var node = doc.RootElement;
        foreach (var segment in segments)
        {
            if (!node.TryGetProperty(segment, out node))
            {
                values = Array.Empty<string>();
                return false;
            }
        }

        values = node.EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
        return true;
    }

    private static System.Text.Json.JsonElement ReadBudgetNode(params string[] segments)
    {
        var budgetPath = ResolveWorkspaceFile("scripts", "performance_budgets.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(budgetPath));
        var node = doc.RootElement;
        foreach (var segment in segments)
        {
            node = node.GetProperty(segment);
        }

        return node.Clone();
    }

    private static int CountReadonlyFields(string source)
    {
        return System.Text.RegularExpressions.Regex.Matches(source, @"(?m)^\s*private\s+readonly\s+").Count;
    }

    private static int CountMemberLikeDeclarations(string source)
    {
        return System.Text.RegularExpressions.Regex.Matches(
            source,
            @"(?m)^\s*(public|private|internal|protected)\s+(static\s+)?([A-Za-z0-9_<>,\[\]\?]+\s+)?[A-Za-z_][A-Za-z0-9_]*\s*\(").Count;
    }

    private static IReadOnlyList<string> ExtractMappedRoutes(string filePath)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            File.ReadAllText(filePath),
            "\\.Map(?:Get|Post|Put|Delete|Patch)\\(\"([^\"]+)\"");

        return matches
            .Select(match => match.Groups[1].Value)
            .Where(static route => !string.IsNullOrWhiteSpace(route))
            .ToArray();
    }

    private static IReadOnlyList<string> FindProjectReferenceCycles(IReadOnlyList<string> projectFiles)
    {
        var graph = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectFile in projectFiles)
        {
            var projectText = File.ReadAllText(projectFile);
            Assert.False(string.IsNullOrWhiteSpace(projectText), $"Project file is empty: {projectFile}");
            var doc = System.Xml.Linq.XDocument.Parse(projectText);
            var references = doc.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
                .Select(element => element.Attribute("Include")?.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, value!)))
                .Where(path => File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            graph[projectFile] = references;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var cycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Dfs(string node)
        {
            if (active.Contains(node))
            {
                var startIndex = stack.FindIndex(path => string.Equals(path, node, StringComparison.OrdinalIgnoreCase));
                if (startIndex >= 0)
                {
                    var cycle = stack.Skip(startIndex).Append(node).Select(ToRelativeProjectPath);
                    cycles.Add(string.Join(" -> ", cycle));
                }

                return;
            }

            if (!visited.Add(node))
            {
                return;
            }

            active.Add(node);
            stack.Add(node);

            foreach (var dependency in graph.GetValueOrDefault(node, Array.Empty<string>()))
            {
                if (graph.ContainsKey(dependency))
                {
                    Dfs(dependency);
                }
            }

            stack.RemoveAt(stack.Count - 1);
            active.Remove(node);
        }

        foreach (var projectFile in graph.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            Dfs(projectFile);
        }

        return cycles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ToRelativeProjectPath(string absolutePath)
    {
        var root = ResolveWorkspaceFile();
        return Path.GetRelativePath(root, absolutePath);
    }
}

