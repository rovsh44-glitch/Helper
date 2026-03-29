using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class InternalObserver : IInternalObserver
    {
        private readonly ISurgicalToolbox _surgery;
        private readonly IPlatformGuard _platforms;

        public InternalObserver(ISurgicalToolbox surgery, IPlatformGuard platforms)
        {
            _surgery = surgery;
            _platforms = platforms;
        }

        public async Task<SystemSnapshot> CaptureSnapshotAsync(string workingDir, CancellationToken ct = default)
        {
            var tree = await _surgery.GetDirectoryTreeAsync(workingDir, 3, ct);
            var caps = _platforms.DetectPlatform();
            
            // Collect any ambient errors (we'd wire this to a shared diagnostic state if needed)
            var recentErrors = new List<string>();

            return new SystemSnapshot(string.IsNullOrWhiteSpace(tree) ? "(Empty Directory)" : tree, recentErrors, caps);
        }
    }
}
