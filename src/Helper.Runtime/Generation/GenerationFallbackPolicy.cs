namespace Helper.Runtime.Generation;

internal static class GenerationFallbackPolicy
{
    public const string Marker = GenerationFallbackRegistry.Marker;

    public static string BuildEmptyMethodBodyFallback()
    {
        return $"""throw new global::System.InvalidOperationException("{GenerationFallbackRegistry.BuildEmptyMethodBodyFallbackMessage()}");""";
    }

    public static string BuildMethodSynthesisFallback(string methodName)
    {
        return $"""throw new global::System.InvalidOperationException("{GenerationFallbackRegistry.BuildMethodSynthesisFallbackMessage(methodName)}");""";
    }

    public static string BuildFileValidationFallback(string rootNamespace, string className, bool isInterface)
    {
        if (isInterface)
        {
            return $@"namespace {rootNamespace}
{{
    public interface {className}
    {{
    }}
}}";
        }

        return $@"namespace {rootNamespace}
{{
    public partial class {className}
    {{
        public void Execute()
        {{
            throw new global::System.InvalidOperationException(""{GenerationFallbackRegistry.BuildFileValidationFallbackMessage()}"");
        }}
    }}
}}";
    }
}

