namespace Helper.GeneratedArtifactValidation.Core;

internal static class GenerationSyntaxProbeBuilder
{
    private const string ProbeBody = "throw new global::System.InvalidOperationException();";

    public static string BuildMethodProbeWrapper(string scopeNamespace, string signature)
        => $"namespace {scopeNamespace} {{ public class Dummy {{ {signature} {{ {ProbeBody} }} }} }}";
}

