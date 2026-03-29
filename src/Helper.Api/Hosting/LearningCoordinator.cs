using Helper.Runtime.Core;

namespace Helper.Api.Hosting;

public interface ILearningCoordinator
{
    Task<IndexingProgress> GetProgressAsync(CancellationToken ct = default);
    Task StartEvolutionAsync(LearningStartRequest? request, CancellationToken ct = default);
    Task PauseEvolutionAsync(CancellationToken ct = default);
    Task StopEvolutionAsync(CancellationToken ct = default);
    Task ResetEvolutionAsync(CancellationToken ct = default);
    Task StartIndexingAsync(LearningStartRequest? request, CancellationToken ct = default);
    Task PauseIndexingAsync(CancellationToken ct = default);
    Task ResetIndexingAsync(CancellationToken ct = default);
}

public sealed class LearningCoordinator : ILearningCoordinator
{
    private readonly ISyntheticLearningService _learner;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public LearningCoordinator(ISyntheticLearningService learner)
    {
        _learner = learner;
    }

    public Task<IndexingProgress> GetProgressAsync(CancellationToken ct = default) => _learner.GetProgressAsync();

    public async Task StartEvolutionAsync(LearningStartRequest? request, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            ApplyTarget(request);
            await _learner.StartEvolutionAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task PauseEvolutionAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            await _learner.PauseEvolutionAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task StopEvolutionAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            await _learner.StopLearningAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task ResetEvolutionAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            await _learner.ResetAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task StartIndexingAsync(LearningStartRequest? request, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            ApplyTarget(request);
            await _learner.StartIndexingAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task PauseIndexingAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            await _learner.PauseIndexingAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task ResetIndexingAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            await _learner.ResetAsync();
        }
        finally
        {
            _mutex.Release();
        }
    }

    private void ApplyTarget(LearningStartRequest? request)
    {
        _learner.SetTargetFile(request?.TargetPath, request?.SingleFileOnly ?? !string.IsNullOrWhiteSpace(request?.TargetPath));
        _learner.SetTargetDomain(request?.TargetDomain);
    }
}

