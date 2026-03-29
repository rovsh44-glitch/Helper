using System;
using System.Text.RegularExpressions;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class CodeSanitizer : ICodeSanitizer
    {
        public string Sanitize(string input, string language = "csharp")
        {
            if (string.IsNullOrEmpty(input)) return "";

            var result = input.Replace("```csharp", "").Replace("```xml", "").Replace("```json", "").Replace("```", "").Trim();

            if (result.StartsWith("xml", StringComparison.OrdinalIgnoreCase)) result = result.Substring(3).Trim();
            if (result.StartsWith("csharp", StringComparison.OrdinalIgnoreCase)) result = result.Substring(6).Trim();

            if (language == "csharp")
            {
                if (result.Contains("[assembly: Guid("))
                {
                    result = Regex.Replace(result, @"\[assembly: Guid\(.*\)\]", "[assembly: Guid(\"00000000-0000-0000-0000-000000000000\")]");
                }

                int startUsing = result.IndexOf("using ");
                int startNs = result.IndexOf("namespace ");
                int start = -1;
                
                if (startUsing >= 0 && startNs >= 0) start = Math.Min(startUsing, startNs);
                else if (startUsing >= 0) start = startUsing;
                else if (startNs >= 0) start = startNs;
                
                if (start > 0) result = result.Substring(start);
            }
            else if (language == "xml" || language == "xaml")
            {
                int start = result.IndexOf("<");
                if (start >= 0) result = result.Substring(start);

                int end = result.LastIndexOf(">");
                if (end >= 0) result = result.Substring(0, end + 1);
            }

            return result.Trim();
        }

        public string FixCsproj(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return "";

            var result = xml;
            result = result.Replace("Microsoft.NET.Sdk.Wpf", "Microsoft.NET.Sdk");
            
            if (!result.Contains("<GenerateAssemblyInfo>"))
            {
                result = result.Replace("</PropertyGroup>", "  <GenerateAssemblyInfo>false</GenerateAssemblyInfo>\n  </PropertyGroup>");
            }

            if (!result.Contains("net8.0-windows"))
            {
                result = Regex.Replace(result, @"<TargetFramework>net\d\.\d(-windows)?</TargetFramework>", "<TargetFramework>net8.0-windows</TargetFramework>", RegexOptions.IgnoreCase);
                result = result.Replace("net6.0", "net8.0-windows")
                               .Replace("net7.0", "net8.0-windows")
                               .Replace("<TargetFramework>net8.0</TargetFramework>", "<TargetFramework>net8.0-windows</TargetFramework>");
            }

            result = result.Replace("System.Windows.Interactivity.Wpf", "Microsoft.Xaml.Behaviors.Wpf");
            result = Regex.Replace(result, 
                @"Include=""Microsoft\.Xaml\.Behaviors\.Wpf""\s+Version=""[\d.]+""", 
                "Include=\"Microsoft.Xaml.Behaviors.Wpf\" Version=\"1.1.39\"", 
                RegexOptions.IgnoreCase);

            return result.Trim();
        }
    }
}

