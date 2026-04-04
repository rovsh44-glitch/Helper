import React, { Suspense, lazy, useCallback, useState } from 'react';
import { Sidebar } from './components/Sidebar';
import { ThoughtStream } from './components/ThoughtStream';
import { PanelResizeHandle } from './components/layout/PanelResizeHandle';
import { HUB_URL } from './services/apiConfig';
import { getSavedResponseStyle } from './services/conversationSession';
import { HelperHubProvider, useHelperHubContext } from './hooks/useHelperHubContext';
import { usePersistentPanelSize } from './hooks/usePersistentPanelSize';
import { ConversationStateProvider } from './contexts/ConversationStateContext';
import { GoalsStateProvider } from './contexts/GoalsStateContext';
import { WorkflowStateProvider } from './contexts/WorkflowStateContext';
import { OperationsRuntimeProvider } from './contexts/OperationsRuntimeContext';
import { BuilderWorkspaceProvider } from './contexts/BuilderWorkspaceContext';
import { ConversationRuntimeController } from './components/views/ConversationRuntimeController';
import type { AppTabKey, ArchitecturePlannerSeed, BuilderLaunchRequest } from './types';

const OrchestratorContainer = lazy(() => import('./components/views/OrchestratorContainer').then(module => ({ default: module.OrchestratorContainer })));
const GoalPanel = lazy(() => import('./components/GoalPanel').then(module => ({ default: module.GoalPanel })));
const StrategicMapView = lazy(() => import('./components/views/StrategicMapView').then(module => ({ default: module.StrategicMapView })));
const BuilderView = lazy(() => import('./components/views/BuilderView').then(module => ({ default: module.BuilderView })));
const ProjectPlannerView = lazy(() => import('./components/views/ProjectPlannerView').then(module => ({ default: module.ProjectPlannerView })));
const SettingsView = lazy(() => import('./components/views/SettingsView').then(module => ({ default: module.SettingsView })));
const EvolutionDashboard = lazy(() => import('./components/views/EvolutionDashboard'));
const RuntimeConsoleView = lazy(() => import('./components/views/RuntimeConsoleView'));
const IndexingPanel = lazy(() => import('./components/IndexingPanel').then(module => ({ default: module.IndexingPanel })));

const HELPER_HUB_URL = HUB_URL;

export default function App() {
  const [activeTab, setActiveTab] = useState<AppTabKey>('orchestrator');
  const [plannerSeed, setPlannerSeed] = useState<ArchitecturePlannerSeed | null>(null);
  const [builderLaunchRequest, setBuilderLaunchRequest] = useState<BuilderLaunchRequest | null>(null);
  const initialResponseStyle = getSavedResponseStyle();
  const { size: navigationWidth, resizeBy: resizeNavigationRail } = usePersistentPanelSize({
    storageKey: 'app-shell.navigation-width',
    defaultSize: 272,
    minSize: 224,
    maxSize: 360,
  });
  const { size: reasoningRailWidth, resizeBy: resizeReasoningRail } = usePersistentPanelSize({
    storageKey: 'app-shell.reasoning-width',
    defaultSize: 320,
    minSize: 272,
    maxSize: 440,
  });

  const renderLazyPanel = useCallback((content: React.ReactNode) => (
    <Suspense fallback={<div className="p-8 text-sm text-slate-500">Loading panel...</div>}>
      {content}
    </Suspense>
  ), []);

  return (
    <HelperHubProvider hubUrl={HELPER_HUB_URL}>
      <ConversationStateProvider initialResponseStyle={initialResponseStyle}>
        <GoalsStateProvider>
          <WorkflowStateProvider>
            <OperationsRuntimeProvider>
              <BuilderWorkspaceProvider>
                <ConversationRuntimeController />
                <div className="flex h-screen bg-slate-950 text-slate-200 overflow-hidden font-sans">
                      <aside className="h-full shrink-0" style={{ width: `${navigationWidth}px` }}>
                        <Sidebar activeTab={activeTab} setActiveTab={setActiveTab} />
                      </aside>
                      <PanelResizeHandle
                        axis="x"
                        title="Resize navigation rail"
                        onResizeDelta={resizeNavigationRail}
                      />
                      <main className="flex-1 relative flex min-w-0">
                        <div className="flex-1 overflow-hidden relative min-w-0">
                      <AppContent
                        activeTab={activeTab}
                        plannerSeed={plannerSeed}
                        builderLaunchRequest={builderLaunchRequest}
                        onOpenPlanner={(seed) => {
                          setPlannerSeed(seed);
                          setActiveTab('planner');
                        }}
                        onOpenBuilder={(request) => {
                          setBuilderLaunchRequest(request);
                          setActiveTab('builder');
                        }}
                        onConsumeBuilderLaunch={() => setBuilderLaunchRequest(null)}
                        renderLazyPanel={renderLazyPanel}
                      />
                        </div>
                        <div className="hidden xl:block">
                          <PanelResizeHandle
                            axis="x"
                            title="Resize reasoning rail"
                            onResizeDelta={(delta) => resizeReasoningRail(-delta)}
                          />
                        </div>
                        <aside className="hidden xl:block h-full shrink-0" style={{ width: `${reasoningRailWidth}px` }}>
                          <ThoughtStream />
                        </aside>
                      </main>
                    </div>
              </BuilderWorkspaceProvider>
            </OperationsRuntimeProvider>
          </WorkflowStateProvider>
        </GoalsStateProvider>
      </ConversationStateProvider>
    </HelperHubProvider>
  );
}

function AppContent({
  activeTab,
  plannerSeed,
  builderLaunchRequest,
  onOpenPlanner,
  onOpenBuilder,
  onConsumeBuilderLaunch,
  renderLazyPanel,
}: {
  activeTab: AppTabKey;
  plannerSeed: ArchitecturePlannerSeed | null;
  builderLaunchRequest: BuilderLaunchRequest | null;
  onOpenPlanner: (seed: ArchitecturePlannerSeed) => void;
  onOpenBuilder: (request: BuilderLaunchRequest) => void;
  onConsumeBuilderLaunch: () => void;
  renderLazyPanel: (content: React.ReactNode) => React.ReactNode;
}) {
  switch (activeTab) {
    case 'orchestrator':
      return renderLazyPanel(<OrchestratorContainer />);
    case 'runtime':
      return renderLazyPanel(<RuntimeConsoleView />);
    case 'strategy':
      return renderLazyPanel(<StrategyPanel onOpenPlanner={onOpenPlanner} />);
    case 'objectives':
      return renderLazyPanel(
        <div className="p-8 h-full bg-slate-950 overflow-y-auto">
          <div className="max-w-2xl mx-auto">
            <GoalPanel />
          </div>
        </div>
      );
    case 'planner':
      return renderLazyPanel(<ProjectPlannerView initialSeed={plannerSeed} onOpenBuilder={onOpenBuilder} />);
    case 'evolution':
      return renderLazyPanel(<EvolutionDashboard />);
    case 'indexing':
      return renderLazyPanel(<IndexingPanel />);
    case 'builder':
      return renderLazyPanel(
        <BuilderView launchRequest={builderLaunchRequest} onLaunchConsumed={onConsumeBuilderLaunch} />,
      );
    case 'settings':
      return renderLazyPanel(<SettingsView />);
    default:
      return <div className="p-8 text-white">Select a module.</div>;
  }
}

function StrategyPanel({ onOpenPlanner }: { onOpenPlanner: (seed: ArchitecturePlannerSeed) => void }) {
  const { currentPlan } = useHelperHubContext();

  return <StrategicMapView initialPlan={currentPlan} onOpenPlanner={onOpenPlanner} />;
}
