using System;
using System.Text.RegularExpressions;

namespace Helper.Runtime.Swarm.Core
{
    public static class SwarmCleaner
    {
        public static string Clean(string raw, string path)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            string cleaned = raw;

            // 1. Extract content between triple backticks if they exist
            var match = Regex.Match(raw, @"```(?:[a-zA-Z]*)\n?(.*?)```", RegexOptions.Singleline);
            if (match.Success)
            {
                cleaned = match.Groups[1].Value;
            }
            else
            {
                // If no backticks, just remove any stray backticks
                cleaned = raw.Replace("```", "");
            }

            cleaned = cleaned.Trim();

            // 2. XML/XAML/CSProj: Must start with '<'
            if (path.EndsWith(".xml") || path.EndsWith(".xaml") || path.EndsWith(".csproj"))
            {
                int start = cleaned.IndexOf("<");
                if (start >= 0) cleaned = cleaned.Substring(start);
                
                // FIX: Remove space after opening bracket if present (e.g. "< Window")
                if (cleaned.StartsWith("< ")) cleaned = "<" + cleaned.Substring(2).TrimStart();

                // FIX: Hallucination cleanup
                if (path.EndsWith(".xaml"))
                {
                    // Remove PlaceholderText (UWP property not in WPF)
                    cleaned = Regex.Replace(cleaned, @"\s*PlaceholderText=""[^""]*""", "");
                }
            }

            // 3. C#: Find first valid code construct
            if (path.EndsWith(".cs"))
            {
                int startUsing = cleaned.IndexOf("using ");
                int startNs = cleaned.IndexOf("namespace ");
                int start = -1;
                
                if (startUsing >= 0 && startNs >= 0) start = Math.Min(startUsing, startNs);
                else if (startUsing >= 0) start = startUsing;
                else if (startNs >= 0) start = startNs;
                
                if (start > 0) cleaned = cleaned.Substring(start);
            }

            return cleaned.Trim();
        }
    }
}

