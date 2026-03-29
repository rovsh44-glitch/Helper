using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;

namespace Helper.Runtime.Infrastructure
{
    public class LocalBuildExecutor : IBuildExecutor
    {
        private readonly IDotnetService _dotnet;

        public LocalBuildExecutor(IDotnetService dotnet)
        {
            _dotnet = dotnet;
        }

        public async Task<List<BuildError>> ExecuteBuildAsync(string workingDirectory, CancellationToken ct = default)
        {
            var profile = PolyglotCompileGateValidator.DetectProfile(workingDirectory);
            if (profile.Kind == PolyglotProjectKind.Dotnet)
            {
                return await _dotnet.BuildAsync(workingDirectory, ct);
            }

            var errors = await PolyglotCompileGateValidator.ValidateNonDotnetSourcesAsync(workingDirectory, profile, ct);
            return errors.ToList();
        }
    }
}

