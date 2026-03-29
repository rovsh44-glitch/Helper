using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class ContextDistiller : IContextDistiller
    {
        public string DistillCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code;

            // 1. Remove single-line comments
            var noComments = Regex.Replace(code, @"//.*", "");
            
            // 2. Remove multi-line comments
            noComments = Regex.Replace(noComments, @"/\*.*?\*/", "", RegexOptions.Singleline);

            // 3. Normalize whitespace and remove empty lines
            var lines = noComments.Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => !string.IsNullOrWhiteSpace(l));

            return string.Join("\n", lines);
        }

        public string DistillPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return prompt;
            
            // Remove excessive newlines and spaces
            return Regex.Replace(prompt, @"\s+", " ").Trim();
        }
    }
}

