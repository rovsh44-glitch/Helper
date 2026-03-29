using System.Collections.Generic;
using System.Linq;
using System.Text;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Swarm.Agents
{
    public class ZuunAssembler
    {
        private readonly IMethodSignatureNormalizer _signatureNormalizer;
        private readonly IMethodBodySemanticGuard _semanticGuard;

        public ZuunAssembler(
            IMethodSignatureNormalizer signatureNormalizer,
            IMethodBodySemanticGuard semanticGuard)
        {
            _signatureNormalizer = signatureNormalizer;
            _semanticGuard = semanticGuard;
        }

        public string AssembleFile(TumenFileTask task, List<ArbanResult> methodBodies, out List<string> diagnostics)
        {
            diagnostics = new List<string>();
            var sb = new StringBuilder();

            foreach (var @using in task.Usings.Distinct(StringComparer.Ordinal))
            {
                sb.AppendLine($"using {@using};");
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {task.Namespace}");
            sb.AppendLine("{");

            var isInterface = task.Role == FileRole.Interface;
            var declarationKeyword = isInterface ? "interface" : "partial class";
            sb.AppendLine($"    public {declarationKeyword} {task.ClassName}");
            sb.AppendLine("    {");

            var seenSignatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var methodTask in task.Methods)
            {
                var signatureNormalization = _signatureNormalizer.Normalize(methodTask.Signature, task.Role, methodTask.Name);
                diagnostics.AddRange(signatureNormalization.Warnings.Select(x => $"{task.Path}/{methodTask.Name}: {x}"));
                if (!signatureNormalization.IsValid || string.IsNullOrWhiteSpace(signatureNormalization.Signature))
                {
                    diagnostics.AddRange(signatureNormalization.Errors.Select(x => $"{task.Path}/{methodTask.Name}: {x}"));
                    continue;
                }

                var signature = signatureNormalization.Signature!;
                if (!seenSignatures.Add(signature))
                {
                    diagnostics.Add($"Duplicate signature removed: {signature}");
                    continue;
                }

                if (isInterface)
                {
                    sb.AppendLine($"        {signature};");
                    sb.AppendLine();
                    continue;
                }

                var body = methodBodies.Find(b => b.MethodName == methodTask.Name)?.Body;
                if (string.IsNullOrWhiteSpace(body))
                {
                    body = GenerationFallbackPolicy.BuildEmptyMethodBodyFallback();
                }

                var safety = _semanticGuard.Guard(signature, body);
                if (safety.UsedFallback)
                {
                    diagnostics.Add(GenerationFallbackRegistry.BuildSemanticGuardInjectedDiagnostic(task.Path, methodTask.Name));
                    diagnostics.AddRange(safety.Diagnostics.Select(x => $"{task.Path}/{methodTask.Name}: {x}"));
                }

                sb.AppendLine($"        {signature}");
                sb.AppendLine("        {");
                foreach (var line in safety.Body.Split('\n'))
                {
                    var trimmed = line.TrimEnd('\r').Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    sb.AppendLine($"            {trimmed}");
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}

