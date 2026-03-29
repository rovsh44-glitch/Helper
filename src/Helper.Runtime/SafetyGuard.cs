using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public interface ISafetyGuard
    {
        Task<bool> ValidateOperationAsync(string operationType, string targetPath, string content = "");
        Task<bool> VerifyModificationAsync(string relativePath, string modifiedContent, CancellationToken ct = default);
    }

    public class SafetyGuard : ISafetyGuard
    {
        private readonly IBuildExecutor _executor;
        private readonly IRecursiveTester _tester;
        private readonly ShadowWorkspace _shadow;
        private readonly string[] _protectedDirectories = { "src/Helper.Runtime", "src/Helper.Api" };
        private bool _allowSelfModification = false;

        public SafetyGuard(IBuildExecutor executor, IRecursiveTester tester, ShadowWorkspace shadow)
        {
            _executor = executor;
            _tester = tester;
            _shadow = shadow;
        }

        public Task<bool> ValidateOperationAsync(string operationType, string targetPath, string content = "")
        {
            var fullPath = Path.GetFullPath(targetPath);
            
            if (operationType == "WRITE" || operationType == "DELETE")
            {
                foreach (var dir in _protectedDirectories)
                {
                    if (fullPath.Contains(Path.GetFullPath(dir)) && !_allowSelfModification)
                    {
                        Console.WriteLine($"[SafetyGuard] ⛔ BLOCK: Attempt to modify core component {targetPath} rejected. Use VerifyModificationAsync first.");
                        return Task.FromResult(false);
                    }
                }
            }

            if (content.Contains("Process.Start") && !content.Contains("dotnet build") && !content.Contains("dotnet test"))
            {
                 Console.WriteLine($"[SafetyGuard] ⚠️ WARNING: Potential unsafe command detected in generated code.");
            }

            return Task.FromResult(true);
        }

        public async Task<bool> VerifyModificationAsync(string relativePath, string modifiedContent, CancellationToken ct = default)
        {
            Console.WriteLine($"[SafetyGuard] 🛡️ Initiating Level 4 Safety Protocol for: {relativePath}");

            // 1. Prepare Shadow Workspace
            bool cloned = await _shadow.CloneAsync(ct);
            if (!cloned) return false;

            // 2. Apply change in shadow
            await _shadow.ApplyPatchAsync(relativePath, modifiedContent, ct);

            // 3. Verify Build
            Console.WriteLine("[SafetyGuard] 🔨 Verifying Build in Shadow Workspace...");
            var errors = await _shadow.VerifyShadowBuildAsync(ct);
            if (errors.Count > 0)
            {
                Console.WriteLine($"[SafetyGuard] ❌ Build FAILED after modification. Rejecting change.");
                return false;
            }

            // 4. Run Self-Tests
            Console.WriteLine("[SafetyGuard] 🧪 Running Self-Tests...");
            var report = await _tester.RunSelfTestsAsync(_shadow.GetShadowPath(), ct);
            if (!report.AllPassed)
            {
                Console.WriteLine($"[SafetyGuard] ❌ Tests FAILED ({report.Failed} failures). Rejecting change.");
                return false;
            }

            Console.WriteLine("[SafetyGuard] ✅ Level 4 Validation SUCCESS. Modification authorized.");
            _allowSelfModification = true; // Temporary authorization for the next write
            
            // Note: In a real system, we might set a timer to reset this or use a more granular token
            return true;
        }
    }
}

