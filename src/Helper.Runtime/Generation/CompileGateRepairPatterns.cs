using System.Text.RegularExpressions;

namespace Helper.Runtime.Generation;

internal static class CompileGateRepairPatterns
{
    internal static readonly Regex CsCodeRegex = new(@"\bCS\d{4}\b", RegexOptions.Compiled);
    internal static readonly Regex MissingTypeRegex = new(@"'(?<type>[A-Za-z_][A-Za-z0-9_\.]*)(?:<.*?>)?'", RegexOptions.Compiled);
    internal static readonly Regex NonNullableMemberRegex = new(@"'(?<name>[A-Za-z_][A-Za-z0-9_]*)'", RegexOptions.Compiled);
    internal static readonly Regex MissingMemberRegex = new(@"definition for '(?<name>[A-Za-z_][A-Za-z0-9_]*)'", RegexOptions.Compiled);
    internal static readonly Regex InvalidUsingTypeRegex = new(
        @"'(?<type>[A-Za-z_][A-Za-z0-9_\.]*)'\s+is a type not a namespace",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex MissingConstructorRegex = new(
        @"'(?<type>[A-Za-z_][A-Za-z0-9_\.]*)(?:<.*?>)?'\s+does not contain a constructor that takes\s+(?<count>\d+)\s+arguments?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex ExistingDynamicFieldRegex = new(@"private\s+dynamic\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=", RegexOptions.Compiled);
    internal static readonly Regex ExistingTypeStubRegex = new(@"\b(?:class|interface|record|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    internal static readonly HashSet<string> SyntaxNoiseCodes = new(StringComparer.Ordinal)
    {
        "CS1001",
        "CS1002",
        "CS1003",
        "CS1056",
        "CS1513",
        "CS1514",
        "CS8803"
    };

    internal static readonly HashSet<string> UnknownSymbolCodes = new(StringComparer.Ordinal)
    {
        "CS0103",
        "CS0117",
        "CS1061"
    };

    internal static readonly IReadOnlyDictionary<string, PackageReferenceSpec> PackageByToken =
        new Dictionary<string, PackageReferenceSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["ManagementObjectSearcher"] = new("System.Management", "9.0.0"),
            ["System.Management"] = new("System.Management", "9.0.0"),
            ["Management"] = new("System.Management", "9.0.0"),
            ["ObservableObject"] = new("CommunityToolkit.Mvvm", "8.2.2"),
            ["RelayCommand"] = new("CommunityToolkit.Mvvm", "8.2.2"),
            ["JsonConvert"] = new("Newtonsoft.Json", "13.0.3"),
            ["JObject"] = new("Newtonsoft.Json", "13.0.3"),
            ["JArray"] = new("Newtonsoft.Json", "13.0.3"),
            ["JToken"] = new("Newtonsoft.Json", "13.0.3"),
            ["QdrantClient"] = new("Qdrant.Client", "1.16.1"),
            ["SpreadsheetDocument"] = new("DocumentFormat.OpenXml", "3.4.1"),
            ["EpubBookRef"] = new("VersOne.Epub", "3.3.5")
        };

    internal static readonly HashSet<string> TypeStubExclusions = new(StringComparer.Ordinal)
    {
        "Task",
        "ValueTask",
        "List",
        "Dictionary",
        "IEnumerable",
        "ICollection",
        "IList",
        "IReadOnlyList",
        "IReadOnlyCollection",
        "String",
        "Object",
        "Exception",
        "EventArgs",
        "StartupEventArgs",
        "RoutedEventArgs",
        "CancellationToken",
        "DateTime",
        "DateTimeOffset",
        "TimeSpan",
        "Guid",
        "Uri"
    };
}

internal sealed record PackageReferenceSpec(string Include, string Version);

