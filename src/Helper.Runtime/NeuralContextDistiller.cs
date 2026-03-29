using System;
using System.Linq;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class NeuralContextDistiller : IContextDistiller
    {
        public NeuralContextDistiller(AILink ai)
        {
            _ = ai;
        }

        public string DistillCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length < 200) return code;
            return BasicCleanup(code);
        }

        public string DistillPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length < 300) return prompt;
            var lines = prompt.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct();

            return string.Join('\n', lines);
        }

        private string BasicCleanup(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code;
            
            // Fixed regex escaping for C# verbatim strings
            var noComments = System.Text.RegularExpressions.Regex.Replace(code, @"//.*", "");
            noComments = System.Text.RegularExpressions.Regex.Replace(noComments, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            
            var lines = noComments.Split('\n')
                .Select(l => l.TrimEnd())
                .Where(l => !string.IsNullOrWhiteSpace(l));
                
            return string.Join("\n", lines);
        }
    }
}

