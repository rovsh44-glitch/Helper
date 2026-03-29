import { startTransition, useEffect, useState } from 'react';

type TabKey = 'runtime' | 'evolution' | 'library' | 'routes';

type AboutDto = {
  productName: string;
  sliceName: string;
  status: string;
  fixtureMode: boolean;
  generatedAtUtc: string;
  publicBoundaries: string[];
};

type StartupReadinessSnapshot = {
  status: string;
  phase: string;
  lifecycleState: string;
  readyForChat: boolean;
  listening: boolean;
  warmupMode: string;
  lastTransitionUtc: string | null;
  startedAtUtc: string;
  listeningAtUtc: string | null;
  minimalReadyAtUtc: string | null;
  warmReadyAtUtc: string | null;
  timeToListeningMs: number | null;
  timeToReadyMs: number | null;
  timeToWarmReadyMs: number | null;
  alerts: string[];
};

type GoalDto = {
  title: string;
  description: string;
};

type EvolutionStatusSnapshot = {
  processedFiles: number;
  totalFiles: number;
  activeTask: string | null;
  goals: GoalDto[];
  isLearning: boolean;
  isIndexing: boolean;
  isEvolution: boolean;
  currentPhase: string;
  fileProgress: number | null;
  pipelineVersion: string | null;
  chunkingStrategy: string | null;
  currentSection: string | null;
  currentPageStart: number | null;
  currentPageEnd: number | null;
  parserVersion: string | null;
  recentLearnings: string[];
  alerts: string[];
};

type LibraryItem = {
  path: string;
  name: string;
  folder: string;
  status: string;
};

type RuntimeLogSemantics = {
  scope: string;
  domain: string;
  operationKind: string;
  summary: string;
  route: string | null;
  correlationId: string | null;
  latencyMs: number | null;
  latencyBucket: string | null;
  degradationReason: string | null;
  markers: string[] | null;
  structured: boolean;
};

type RuntimeLogEntry = {
  sourceId: string;
  lineNumber: number;
  text: string;
  severity: string;
  timestampLabel: string | null;
  isContinuation: boolean;
  semantics: RuntimeLogSemantics | null;
};

type RuntimeLogSource = {
  id: string;
  label: string;
  displayPath: string;
  sizeBytes: number;
  lastWriteTimeUtc: string | null;
  totalLines: number;
  isPrimary: boolean;
};

type RuntimeLogsSnapshot = {
  schemaVersion: number;
  semanticsVersion: string;
  generatedAtUtc: string;
  sources: RuntimeLogSource[];
  entries: RuntimeLogEntry[];
  alerts: string[];
};

type RouteTelemetryBucket = {
  key: string;
  count: number;
};

type RouteTelemetryEvent = {
  recordedAtUtc: string;
  channel: string;
  operationKind: string;
  routeKey: string;
  quality: string;
  outcome: string;
  confidence: number | null;
  modelRoute: string | null;
  correlationId: string | null;
  intentSource: string | null;
  executionMode: string | null;
  budgetProfile: string | null;
  workloadClass: string | null;
  degradationReason: string | null;
  routeMatched: boolean;
  requiresClarification: boolean;
  budgetExceeded: boolean;
  smokePassed: boolean | null;
  signals: string[] | null;
};

type RouteTelemetrySnapshot = {
  schemaVersion: number;
  generatedAtUtc: string;
  totalEvents: number;
  channels: RouteTelemetryBucket[];
  operationKinds: RouteTelemetryBucket[];
  routes: RouteTelemetryBucket[];
  qualities: RouteTelemetryBucket[];
  modelRoutes: RouteTelemetryBucket[];
  recent: RouteTelemetryEvent[];
  alerts: string[];
};

type SliceState = {
  about: AboutDto;
  readiness: StartupReadinessSnapshot;
  evolution: EvolutionStatusSnapshot;
  library: LibraryItem[];
  logs: RuntimeLogsSnapshot;
  routes: RouteTelemetrySnapshot;
};

const tabs: Array<{ key: TabKey; label: string; description: string }> = [
  { key: 'runtime', label: 'Runtime Console', description: 'Sanitized log review with semantics and severity buckets.' },
  { key: 'evolution', label: 'Evolution', description: 'Fixture-backed progress, goals, and indexing state.' },
  { key: 'library', label: 'Library Indexing', description: 'Queue snapshot for local-first knowledge ingestion.' },
  { key: 'routes', label: 'Route Telemetry', description: 'Recent route quality, buckets, and degradation alerts.' },
];

function createApiUrl(path: string) {
  const configuredBase = import.meta.env.VITE_RUNTIME_SLICE_API_BASE as string | undefined;
  const fallbackBase = import.meta.env.DEV ? 'http://localhost:5076' : '';
  const base = configuredBase ?? fallbackBase;
  return new URL(path, base || window.location.origin).toString();
}

async function fetchJson<T>(path: string): Promise<T> {
  const response = await fetch(createApiUrl(path));
  if (!response.ok) {
    throw new Error(`Request failed for ${path}: ${response.status}`);
  }

  return response.json() as Promise<T>;
}

async function loadSliceState(): Promise<SliceState> {
  const [about, readiness, evolution, library, logs, routes] = await Promise.all([
    fetchJson<AboutDto>('/api/about'),
    fetchJson<StartupReadinessSnapshot>('/api/readiness'),
    fetchJson<EvolutionStatusSnapshot>('/api/evolution/status'),
    fetchJson<LibraryItem[]>('/api/evolution/library'),
    fetchJson<RuntimeLogsSnapshot>('/api/runtime/logs'),
    fetchJson<RouteTelemetrySnapshot>('/api/telemetry/routes'),
  ]);

  return { about, readiness, evolution, library, logs, routes };
}

export function App() {
  const [activeTab, setActiveTab] = useState<TabKey>('runtime');
  const [state, setState] = useState<SliceState | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshStamp, setRefreshStamp] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function run() {
      setLoading(true);
      setError(null);
      try {
        const next = await loadSliceState();
        if (!cancelled) {
          startTransition(() => {
            setState(next);
            setRefreshStamp(new Date().toISOString());
          });
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Unknown slice error');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void run();
    return () => {
      cancelled = true;
    };
  }, []);

  const activeMeta = tabs.find(tab => tab.key === activeTab)!;

  return (
    <div className="slice-app">
      <aside className="slice-sidebar">
        <div className="brand-block">
          <p className="eyebrow">Public-safe runnable slice</p>
          <h1>Helper Runtime Review</h1>
          <p className="lede">
            Narrow operator-facing proof surface for runtime visibility, telemetry, and indexing review.
          </p>
        </div>
        <div className="status-card">
          <div className="status-chip">Fixture Mode</div>
          <p>This build is backed by sanitized sample data and exposes read-only endpoints only.</p>
          {state ? (
            <ul className="boundary-list">
              {state.about.publicBoundaries.map(boundary => (
                <li key={boundary}>{boundary}</li>
              ))}
            </ul>
          ) : null}
        </div>
        <nav className="tab-list" aria-label="Slice panels">
          {tabs.map(tab => (
            <button
              key={tab.key}
              type="button"
              className={tab.key === activeTab ? 'tab-button active' : 'tab-button'}
              onClick={() => startTransition(() => setActiveTab(tab.key))}
            >
              <span>{tab.label}</span>
              <small>{tab.description}</small>
            </button>
          ))}
        </nav>
      </aside>

      <main className="slice-main">
        <header className="slice-header">
          <div>
            <p className="eyebrow">Runtime review surface</p>
            <h2>{activeMeta.label}</h2>
            <p className="panel-description">{activeMeta.description}</p>
          </div>
          <div className="header-meta">
            <span>{refreshStamp ? `Loaded ${new Date(refreshStamp).toLocaleTimeString()}` : 'Not loaded yet'}</span>
            <button type="button" className="refresh-button" onClick={() => window.location.reload()}>
              Reload
            </button>
          </div>
        </header>

        {loading ? <section className="panel-shell">Loading slice state...</section> : null}
        {error ? <section className="panel-shell error-panel">{error}</section> : null}
        {!loading && !error && state ? (
          <>
            <section className="readiness-strip">
              <div className="metric-card">
                <span>Status</span>
                <strong>{state.readiness.status}</strong>
              </div>
              <div className="metric-card">
                <span>Phase</span>
                <strong>{state.readiness.phase}</strong>
              </div>
              <div className="metric-card">
                <span>Ready</span>
                <strong>{state.readiness.readyForChat ? 'yes' : 'no'}</strong>
              </div>
              <div className="metric-card">
                <span>Warmup</span>
                <strong>{state.readiness.warmupMode}</strong>
              </div>
            </section>

            {activeTab === 'runtime' ? <RuntimePanel snapshot={state.logs} /> : null}
            {activeTab === 'evolution' ? <EvolutionPanel snapshot={state.evolution} /> : null}
            {activeTab === 'library' ? <LibraryPanel items={state.library} /> : null}
            {activeTab === 'routes' ? <RoutesPanel snapshot={state.routes} /> : null}
          </>
        ) : null}
      </main>
    </div>
  );
}

function RuntimePanel({ snapshot }: { snapshot: RuntimeLogsSnapshot }) {
  return (
    <section className="panel-grid">
      <article className="panel-shell compact-panel">
        <h3>Sources</h3>
        <ul className="source-list">
          {snapshot.sources.map(source => (
            <li key={source.id}>
              <strong>{source.label}</strong>
              <span>{source.displayPath}</span>
              <small>{source.totalLines} lines</small>
            </li>
          ))}
        </ul>
      </article>
      <article className="panel-shell log-panel">
        <h3>Recent Entries</h3>
        {snapshot.alerts.length > 0 ? (
          <div className="alert-strip">
            {snapshot.alerts.map(alert => (
              <span key={alert}>{alert}</span>
            ))}
          </div>
        ) : null}
        <div className="log-list">
          {snapshot.entries.map(entry => (
            <div key={`${entry.sourceId}-${entry.lineNumber}`} className={`log-entry severity-${entry.severity}`}>
              <div className="log-meta">
                <span>{entry.timestampLabel ?? 'no-ts'}</span>
                <span>{entry.semantics?.operationKind ?? 'runtime'}</span>
                <span>{entry.semantics?.domain ?? 'runtime'}</span>
              </div>
              <pre>{entry.text}</pre>
              {entry.semantics ? (
                <div className="marker-row">
                  {(entry.semantics.markers ?? []).map(marker => (
                    <span key={marker}>{marker}</span>
                  ))}
                </div>
              ) : null}
            </div>
          ))}
        </div>
      </article>
    </section>
  );
}

function EvolutionPanel({ snapshot }: { snapshot: EvolutionStatusSnapshot }) {
  return (
    <section className="panel-grid">
      <article className="panel-shell">
        <h3>Progress</h3>
        <div className="stats-grid">
          <div className="stat">
            <span>Processed</span>
            <strong>{snapshot.processedFiles}</strong>
          </div>
          <div className="stat">
            <span>Total</span>
            <strong>{snapshot.totalFiles}</strong>
          </div>
          <div className="stat">
            <span>Phase</span>
            <strong>{snapshot.currentPhase}</strong>
          </div>
          <div className="stat">
            <span>Pipeline</span>
            <strong>{snapshot.pipelineVersion ?? 'n/a'}</strong>
          </div>
        </div>
        <div className="progress-bar" aria-hidden="true">
          <div style={{ width: `${Math.round((snapshot.fileProgress ?? 0) * 100)}%` }} />
        </div>
        <p className="muted">{snapshot.activeTask ?? 'No active task'}</p>
      </article>
      <article className="panel-shell">
        <h3>Goals</h3>
        <div className="goal-list">
          {snapshot.goals.map(goal => (
            <div key={goal.title} className="goal-card">
              <strong>{goal.title}</strong>
              <p>{goal.description}</p>
            </div>
          ))}
        </div>
      </article>
      <article className="panel-shell">
        <h3>Alerts</h3>
        <ul className="simple-list">
          {snapshot.alerts.map(alert => (
            <li key={alert}>{alert}</li>
          ))}
          {snapshot.recentLearnings.map(item => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </article>
    </section>
  );
}

function LibraryPanel({ items }: { items: LibraryItem[] }) {
  return (
    <section className="panel-shell">
      <h3>Queue Snapshot</h3>
      <div className="table-shell">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Folder</th>
              <th>Status</th>
              <th>Path</th>
            </tr>
          </thead>
          <tbody>
            {items.map(item => (
              <tr key={item.path}>
                <td>{item.name}</td>
                <td>{item.folder}</td>
                <td><span className={`status-pill status-${item.status}`}>{item.status}</span></td>
                <td>{item.path}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function RoutesPanel({ snapshot }: { snapshot: RouteTelemetrySnapshot }) {
  return (
    <section className="panel-grid">
      <article className="panel-shell compact-panel">
        <h3>Alerts</h3>
        <div className="alert-strip">
          {snapshot.alerts.length > 0 ? snapshot.alerts.map(alert => <span key={alert}>{alert}</span>) : <span>No active alerts.</span>}
        </div>
        <h3>Qualities</h3>
        <div className="bucket-grid">
          {snapshot.qualities.map(bucket => (
            <div key={bucket.key} className="bucket-card">
              <span>{bucket.key}</span>
              <strong>{bucket.count}</strong>
            </div>
          ))}
        </div>
      </article>
      <article className="panel-shell">
        <h3>Recent Route Events</h3>
        <div className="table-shell">
          <table>
            <thead>
              <tr>
                <th>Time</th>
                <th>Route</th>
                <th>Quality</th>
                <th>Outcome</th>
                <th>Reason</th>
              </tr>
            </thead>
            <tbody>
              {snapshot.recent.map(event => (
                <tr key={`${event.correlationId ?? event.routeKey}-${event.recordedAtUtc}`}>
                  <td>{new Date(event.recordedAtUtc).toLocaleTimeString()}</td>
                  <td>{event.routeKey}</td>
                  <td>{event.quality}</td>
                  <td>{event.outcome}</td>
                  <td>{event.degradationReason ?? 'n/a'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
    </section>
  );
}
