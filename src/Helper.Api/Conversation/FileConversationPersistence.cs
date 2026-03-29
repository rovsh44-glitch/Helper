using System.Text.Json;
using Helper.Api.Hosting;
using Helper.Runtime.Infrastructure;

namespace Helper.Api.Conversation;

public sealed record ConversationPersistenceHealthSnapshot(
    bool Enabled,
    bool Ready,
    bool Loaded,
    bool LastFlushSucceeded,
    int PendingDirtyConversations,
    DateTimeOffset? LastJournalWriteAtUtc,
    DateTimeOffset? LastSnapshotAtUtc,
    string SnapshotPath,
    string JournalPath,
    IReadOnlyList<string> Alerts);

public interface IConversationPersistenceHealth
{
    ConversationPersistenceHealthSnapshot GetSnapshot(int pendingDirtyConversations = 0);
}

public interface IConversationPersistenceEngine : IConversationPersistenceHealth, IDisposable
{
    IReadOnlyList<ConversationState> Load();
    void FlushDirty(IReadOnlyCollection<ConversationState> dirtyStates, IReadOnlyCollection<ConversationState> allStates);
}

public sealed class FileConversationPersistence : IConversationPersistenceEngine
{
    private const int CurrentPersistenceSchemaVersion = 6;

    private readonly object _sync = new();
    private readonly string? _snapshotPath;
    private readonly string? _legacySnapshotPath;
    private readonly string? _journalPath;
    private readonly IConversationStageMetricsService? _stageMetrics;
    private readonly int _journalCompactionThreshold;

    private bool _loaded;
    private bool _lastFlushSucceeded = true;
    private int _journalEntriesSinceSnapshot;
    private DateTimeOffset? _lastJournalWriteAtUtc;
    private DateTimeOffset? _lastSnapshotAtUtc;
    private readonly List<string> _alerts = new();

    public FileConversationPersistence(string? basePath, IConversationStageMetricsService? stageMetrics = null)
    {
        if (!string.IsNullOrWhiteSpace(basePath))
        {
            var fullPath = Path.GetFullPath(basePath);
            var directory = Path.GetDirectoryName(fullPath) ?? HelperWorkspacePathResolver.ResolveLogsRoot();
            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            var extension = Path.GetExtension(fullPath);
            _snapshotPath = fullPath;
            _legacySnapshotPath = Path.Combine(directory, fileName + ".snapshot" + extension);
            _journalPath = Path.Combine(directory, fileName + ".journal.jsonl");
        }

        _stageMetrics = stageMetrics;
        _journalCompactionThreshold = ReadCompactionThreshold();
    }

    public IReadOnlyList<ConversationState> Load()
    {
        if (string.IsNullOrWhiteSpace(_snapshotPath) || string.IsNullOrWhiteSpace(_journalPath))
        {
            _loaded = true;
            return Array.Empty<ConversationState>();
        }

        lock (_sync)
        {
            var byConversation = new Dictionary<string, PersistedConversationState>(StringComparer.OrdinalIgnoreCase);
            var migratedFromLegacy = false;
            try
            {
                var snapshotPath = ResolveReadableSnapshotPath();
                if (!string.IsNullOrWhiteSpace(snapshotPath) && File.Exists(snapshotPath))
                {
                    var raw = File.ReadAllText(snapshotPath);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        var snapshotItems = ConversationPersistenceModelMapper.DeserializeSnapshot(raw, CurrentPersistenceSchemaVersion, out var snapshotMigrated);
                        migratedFromLegacy |= snapshotMigrated;
                        migratedFromLegacy |= !string.Equals(snapshotPath, _snapshotPath, StringComparison.OrdinalIgnoreCase);
                        foreach (var item in snapshotItems)
                        {
                            byConversation[item.Id] = item;
                        }
                    }
                }

                if (File.Exists(_journalPath))
                {
                    foreach (var line in File.ReadLines(_journalPath))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var entry = JsonSerializer.Deserialize<PersistedConversationJournalEntry>(line);
                        if (entry?.Conversation == null || string.IsNullOrWhiteSpace(entry.Conversation.Id))
                        {
                            continue;
                        }

                        byConversation[entry.Conversation.Id] = entry.Conversation;
                        _journalEntriesSinceSnapshot++;
                    }
                }

                _loaded = true;
                var loadedStates = byConversation.Values
                    .Select(ConversationPersistenceModelMapper.FromPersistenceModel)
                    .ToList();

                if (migratedFromLegacy && loadedStates.Count > 0)
                {
                    WriteSnapshot(loadedStates);
                    _lastFlushSucceeded = true;
                }

                return loadedStates;
            }
            catch (Exception ex)
            {
                _alerts.Add($"Conversation persistence load failed: {ex.Message}");
                _loaded = false;
                return Array.Empty<ConversationState>();
            }
        }
    }

    public void FlushDirty(IReadOnlyCollection<ConversationState> dirtyStates, IReadOnlyCollection<ConversationState> allStates)
    {
        if (string.IsNullOrWhiteSpace(_snapshotPath) || string.IsNullOrWhiteSpace(_journalPath) || dirtyStates.Count == 0)
        {
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_snapshotPath)!);

                using (var stream = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream))
                {
                    foreach (var state in dirtyStates)
                    {
                        var line = JsonSerializer.Serialize(new PersistedConversationJournalEntry
                        {
                            SchemaVersion = CurrentPersistenceSchemaVersion,
                            PersistedAtUtc = DateTimeOffset.UtcNow,
                            Conversation = ConversationPersistenceModelMapper.ToPersistenceModel(state)
                        });
                        writer.WriteLine(line);
                        _journalEntriesSinceSnapshot++;
                    }
                }

                _lastJournalWriteAtUtc = DateTimeOffset.UtcNow;

                if (_journalEntriesSinceSnapshot >= _journalCompactionThreshold)
                {
                    WriteSnapshot(allStates);
                }

                _lastFlushSucceeded = true;
            }

            _stageMetrics?.Record("persistence", sw.ElapsedMilliseconds, success: true);
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _lastFlushSucceeded = false;
                _alerts.Add($"Conversation persistence flush failed: {ex.Message}");
                TrimAlerts();
            }

            _stageMetrics?.Record("persistence", sw.ElapsedMilliseconds, success: false);
        }
    }

    public ConversationPersistenceHealthSnapshot GetSnapshot(int pendingDirtyConversations = 0)
    {
        lock (_sync)
        {
            var alerts = _alerts.ToArray();
            return new ConversationPersistenceHealthSnapshot(
                Enabled: !string.IsNullOrWhiteSpace(_snapshotPath),
                Ready: string.IsNullOrWhiteSpace(_snapshotPath) || (_loaded && _lastFlushSucceeded),
                Loaded: _loaded,
                LastFlushSucceeded: _lastFlushSucceeded,
                PendingDirtyConversations: Math.Max(0, pendingDirtyConversations),
                LastJournalWriteAtUtc: _lastJournalWriteAtUtc,
                LastSnapshotAtUtc: _lastSnapshotAtUtc,
                SnapshotPath: _snapshotPath ?? string.Empty,
                JournalPath: _journalPath ?? string.Empty,
                Alerts: alerts);
        }
    }

    public void Dispose()
    {
    }

    private string? ResolveReadableSnapshotPath()
    {
        if (!string.IsNullOrWhiteSpace(_snapshotPath) && File.Exists(_snapshotPath))
        {
            return _snapshotPath;
        }

        if (!string.IsNullOrWhiteSpace(_legacySnapshotPath) && File.Exists(_legacySnapshotPath))
        {
            return _legacySnapshotPath;
        }

        return _snapshotPath;
    }

    private void WriteSnapshot(IReadOnlyCollection<ConversationState> allStates)
    {
        var envelope = new PersistedConversationEnvelope
        {
            SchemaVersion = CurrentPersistenceSchemaVersion,
            SavedAt = DateTimeOffset.UtcNow,
            Conversations = allStates.Select(ConversationPersistenceModelMapper.ToPersistenceModel).ToList()
        };

        var json = JsonSerializer.Serialize(envelope);
        var tempPath = _snapshotPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Copy(tempPath, _snapshotPath!, overwrite: true);
        File.Delete(tempPath);
        File.WriteAllText(_journalPath!, string.Empty);
        _journalEntriesSinceSnapshot = 0;
        _lastSnapshotAtUtc = DateTimeOffset.UtcNow;
    }

    private static int ReadCompactionThreshold()
    {
        var raw = Environment.GetEnvironmentVariable("HELPER_CONVERSATION_JOURNAL_COMPACTION_THRESHOLD");
        return int.TryParse(raw, out var parsed)
            ? Math.Clamp(parsed, 5, 500)
            : 25;
    }

    private void TrimAlerts()
    {
        while (_alerts.Count > 10)
        {
            _alerts.RemoveAt(0);
        }
    }
}

