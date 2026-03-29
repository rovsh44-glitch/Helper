using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class RecursiveTester : IRecursiveTester
    {
        private readonly AILink _ai;
        private readonly IDotnetService _dotnet;

        public RecursiveTester(AILink ai, IDotnetService dotnet)
        {
            _ai = ai;
            _dotnet = dotnet;
        }

        public async Task<GeneratedFile> GenerateTestForComponentAsync(string sourceCode, string componentName, CancellationToken ct = default)
        {
            var prompt = $@"
            ACT AS A TEST ENGINEER.
            OBJECTIVE: Create an xUnit test class for the following C# component.
            
            COMPONENT CODE:
            {sourceCode}
            
            STRICT RULES:
            1. Use xUnit and Moq.
            2. Cover critical edge cases.
            3. Output ONLY RAW C# CODE. No talk.
            
            CLASS NAME: {componentName}Tests";

            var testCode = await _ai.AskAsync(prompt, ct);
            return new GeneratedFile($"{componentName}Tests.cs", testCode.Replace("```csharp", "").Replace("```", "").Trim(), "csharp");
        }

        public async Task<TestReport> RunSelfTestsAsync(string projectPath, CancellationToken ct = default)
        {
            return await _dotnet.TestAsync(projectPath, ct);
        }
    }
}

