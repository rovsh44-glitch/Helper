using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class IntegrityAuditor
    {
        private readonly AILink _ai;

        public IntegrityAuditor(AILink ai) => _ai = ai;

        public async Task<(bool Valid, string Feedback)> AuditProjectAsync(List<GeneratedFile> files, string topic, CancellationToken ct = default)
        {
            var auditContext = new System.Text.StringBuilder();
            foreach (var f in files)
            {
                auditContext.AppendLine($"File: {f.RelativePath}");
                // Essential structure for audit
                var snippet = f.Content.Length > 1000 ? f.Content.Substring(0, 1000) + "..." : f.Content;
                auditContext.AppendLine(snippet);
                auditContext.AppendLine("---");
            }

            var prompt = $@"
            ACT AS A SENIOR CODE REVIEWER.
            
            PROJECT TOPIC: {topic}
            
            CODE TO REVIEW:
            {auditContext}
            
            OBJECTIVE: Detect if the project is a functional application or just a placeholder skeleton.
            
            CHECKLIST:
            1. Are there placeholders like '// logic here', 'NotImplementedException', or empty methods?
            2. Do WPF Bindings in XAML match public properties in C#?
            3. Are resources (Styles/Themes) actually registered in App.xaml?
            4. Is the core logic (e.g. calculation for a calculator) actually implemented?
            5. GRID SAFETY: Does the number of RowDefinitions/ColumnDefinitions in XAML match the highest index used in child elements?
            6. COMMAND COVERAGE: Does MainViewModel implement ALL commands used in XAML (e.g. OperatorCommand, DecimalCommand, EqualsCommand, ClearCommand)?
            7. INTERFACE: Does IMainViewModel exist and is it implemented by MainViewModel?
            8. RESOURCE INTEGRITY: Do all ResourceDictionary Source paths in App.xaml point to files that actually exist in the generated files list?
            9. NAMESPACE SYNC: Does the x:Class in XAML files match the C# namespace exactly?
            10. DATACONTEXT BOOTSTRAP: You MUST check if DataContext is assigned. If it is NOT assigned in MainWindow.xaml.cs (e.g. 'DataContext = new MainViewModel();') AND not assigned in MainWindow.xaml (e.g. '<Window.DataContext>'), then IsValid MUST be false.
            11. LOCAL XMLNS: Does the XAML root contain the local namespace declaration (xmlns:local)?
            
            Output ONLY valid JSON:
            {{
                ""IsValid"": true/false,
                ""Feedback"": ""Detailed list of missing logic or mismatches""
            }}";

            var response = await _ai.AskAsync(prompt, ct, "qwen2.5-coder:14b");
            var json = response.Contains("```json") ? response.Split("```json")[1].Split("```")[0].Trim() : response.Trim();

            try 
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                bool isValid = doc.RootElement.GetProperty("IsValid").GetBoolean();
                string feedback = doc.RootElement.GetProperty("Feedback").GetString() ?? "";
                return (isValid, feedback);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[IntegrityAuditor] Failed to parse audit response: {ex.Message}");
                return (false, "Audit failed to parse result.");
            }
        }
    }
}

