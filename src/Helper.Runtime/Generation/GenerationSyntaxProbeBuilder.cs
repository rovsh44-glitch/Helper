namespace Helper.Runtime.Generation;

internal static class GenerationSyntaxProbeBuilder
{
    private const string ProbeBody = "throw new global::System.InvalidOperationException();";

    public static string BuildMethodProbeWrapper(string scopeNamespace, string signature)
    {
        return BuildMethodWrapper(scopeNamespace, signature, ProbeBody);
    }

    public static string BuildSemanticGuardWrapper(string methodSignature, string methodBody)
    {
        return $@"using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace SemanticGuard
{{
    public class Dummy
    {{
        {methodSignature}
        {{
            {methodBody}
        }}
    }}
}}";
    }

    private static string BuildMethodWrapper(string scopeNamespace, string signature, string body)
    {
        return $"namespace {scopeNamespace} {{ public class Dummy {{ {signature} {{ {body} }} }} }}";
    }
}

