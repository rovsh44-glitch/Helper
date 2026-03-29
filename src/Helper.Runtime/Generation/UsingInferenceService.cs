using Helper.Runtime.Core;

namespace Helper.Runtime.Generation;

public sealed class UsingInferenceService : IUsingInferenceService
{
    private static readonly IReadOnlyDictionary<string, string> InferenceMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ObservableCollection"] = "System.Collections.ObjectModel",
        ["Task"] = "System.Threading.Tasks",
        ["ValueTask"] = "System.Threading.Tasks",
        ["ICommand"] = "System.Windows.Input",
        ["RoutedEventArgs"] = "System.Windows",
        ["ExecutedRoutedEventArgs"] = "System.Windows.Input",
        ["CanExecuteRoutedEventArgs"] = "System.Windows.Input",
        ["INotifyPropertyChanged"] = "System.ComponentModel",
        ["PropertyChangedEventArgs"] = "System.ComponentModel",
        ["CancellationToken"] = "System.Threading"
    };

    private readonly ITypeTokenExtractor _tokenExtractor;

    public UsingInferenceService(ITypeTokenExtractor tokenExtractor)
    {
        _tokenExtractor = tokenExtractor;
    }

    public IReadOnlyCollection<string> InferUsings(
        string rootNamespace,
        string relativePath,
        FileRole role,
        IReadOnlyList<ArbanMethodTask> methods)
    {
        var usings = new HashSet<string>(StringComparer.Ordinal)
        {
            "System",
            "System.Collections.Generic",
            "System.Linq"
        };

        if (role == FileRole.View || relativePath.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
        {
            usings.Add("System.Windows");
            usings.Add("System.Windows.Input");
        }

        foreach (var method in methods)
        {
            foreach (var token in _tokenExtractor.ExtractFromSignature(method.Signature))
            {
                var inferred = ResolveUsingForTypeToken(token);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    usings.Add(inferred);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(rootNamespace))
        {
            usings.Add(rootNamespace);
        }

        return usings.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public string? ResolveUsingForTypeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return InferenceMap.TryGetValue(token.Trim(), out var ns) ? ns : null;
    }
}

