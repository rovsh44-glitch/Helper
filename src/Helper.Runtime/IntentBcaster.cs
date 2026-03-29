using System;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class IntentBcaster : IIntentBcaster
    {
        public Task BroadcastIntentAsync(string action, string rationale, Action<string>? onProgress, CancellationToken ct = default)
        {
            var msg = $"💬 [Intent] Action: {action} | Rationale: {rationale}";
            onProgress?.Invoke(msg);
            return Task.CompletedTask;
        }
    }
}
