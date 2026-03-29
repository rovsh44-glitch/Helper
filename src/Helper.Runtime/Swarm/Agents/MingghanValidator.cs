using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Swarm.Core;

namespace Helper.Runtime.Swarm.Agents
{
    public class MingghanValidator
    {
        private readonly RoslynLiveCorrector _corrector = new();

        public async Task<List<string>> ValidateModuleAsync(string folderPath, CancellationToken ct = default)
        {
            var errors = new List<string>();
            var files = System.IO.Directory.GetFiles(folderPath, "*.cs");

            foreach (var file in files)
            {
                var code = await System.IO.File.ReadAllTextAsync(file, ct);
                var validation = _corrector.ValidateSyntax(code);
                if (!validation.Valid)
                {
                    errors.AddRange(validation.Errors.Select(e => $"File {System.IO.Path.GetFileName(file)}: {e}"));
                }
            }

            return errors;
        }
    }
}

