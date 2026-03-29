using Helper.Runtime.Core;

namespace Helper.Runtime.Swarm;

internal sealed class TumenCritiqueRunner
{
    private readonly ICriticService _critic;
    private readonly TimeSpan _timeout;

    public TumenCritiqueRunner(ICriticService critic, TimeSpan timeout)
    {
        _critic = critic;
        _timeout = timeout;
    }

    public async Task<CritiqueResult> RunAsync(string sourceData, string draft, string context, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var critiqueTask = _critic.CritiqueAsync(sourceData, draft, context, linkedCts.Token);
        var timeoutTask = Task.Delay(_timeout, ct);
        var completed = await Task.WhenAny(critiqueTask, timeoutTask).ConfigureAwait(false);
        if (completed == critiqueTask)
        {
            return await critiqueTask.ConfigureAwait(false);
        }

        linkedCts.Cancel();
        return new CritiqueResult(true, $"Critic timeout after {_timeout.TotalSeconds:0}s. Candidate accepted.", null);
    }
}

