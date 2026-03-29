using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Helper.Runtime.Infrastructure
{
    internal static class DotnetSandboxSupport
    {
        private const string IntermediateRootEnv = "HELPER_MSBUILD_INTERMEDIATE_ROOT";

        public static string AppendSandboxIntermediateProperties(string arguments, string workingDirectory)
        {
            var sandboxRoot = ResolveIntermediateRoot(workingDirectory);
            if (string.IsNullOrWhiteSpace(sandboxRoot))
            {
                return arguments;
            }

            var intermediateRoot = EnsureTrailingSeparator(Path.Combine(sandboxRoot, "obj"));
            Directory.CreateDirectory(intermediateRoot);

            return string.Join(
                " ",
                arguments,
                $@"-p:BaseIntermediateOutputPath=""{EscapeArgument(intermediateRoot)}""",
                $@"-p:MSBuildProjectExtensionsPath=""{EscapeArgument(intermediateRoot)}"""
            );
        }

        private static string? ResolveIntermediateRoot(string workingDirectory)
        {
            var configuredRoot = Environment.GetEnvironmentVariable(IntermediateRootEnv);
            var baseRoot = !string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.GetFullPath(configuredRoot)
                : ResolveCodexSandboxRoot();

            if (string.IsNullOrWhiteSpace(baseRoot))
            {
                return null;
            }

            Directory.CreateDirectory(baseRoot);

            var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
            var trimmedWorkingDirectory = fullWorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var leafName = Path.GetFileName(trimmedWorkingDirectory);
            if (string.IsNullOrWhiteSpace(leafName))
            {
                leafName = "workspace";
            }

            return Path.Combine(baseRoot, $"{SanitizeSegment(leafName)}-{BuildFingerprint(fullWorkingDirectory)}");
        }

        private static string? ResolveCodexSandboxRoot()
        {
            var isCodexSandbox =
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_THREAD_ID")) ||
                string.Equals(Environment.GetEnvironmentVariable("CODEX_SANDBOX_NETWORK_DISABLED"), "1", StringComparison.Ordinal);
            if (!isCodexSandbox)
            {
                return null;
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                return null;
            }

            return Path.Combine(userProfile, ".codex", "memories", "msbuild-intermediate");
        }

        private static string BuildFingerprint(string value)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
        }

        private static string SanitizeSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '-' : ch);
            }

            return builder.ToString();
        }

        private static string EnsureTrailingSeparator(string path)
            => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;

        private static string EscapeArgument(string value)
        {
            var escaped = value.Replace("\"", "\\\"");
            if (escaped.EndsWith(Path.DirectorySeparatorChar) || escaped.EndsWith(Path.AltDirectorySeparatorChar))
            {
                escaped += escaped[^1];
            }

            return escaped;
        }
    }
}

