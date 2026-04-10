import React from 'react';
import { CapabilityCoveragePanel } from '../CapabilityCoveragePanel';
import { HumanLikeConversationDashboardPanel } from '../HumanLikeConversationDashboardPanel';
import { SettingsAlertsPanel } from '../settings/SettingsAlertsPanel';
import { SettingsSecurityPanel } from '../settings/SettingsSecurityPanel';
import { SettingsInfrastructurePanel } from '../settings/SettingsInfrastructurePanel';
import { SettingsConversationStylePanel } from '../settings/SettingsConversationStylePanel';
import { SettingsMemoryPolicyPanel } from '../settings/SettingsMemoryPolicyPanel';
import { SettingsMemoryItemsPanel } from '../settings/SettingsMemoryItemsPanel';
import { SettingsPersonalizationPanel } from '../settings/SettingsPersonalizationPanel';
import { SettingsProjectContextPanel } from '../settings/SettingsProjectContextPanel';
import { SettingsProviderProfilesPanel } from '../settings/SettingsProviderProfilesPanel';
import { SettingsRuntimeDoctorPanel } from '../settings/SettingsRuntimeDoctorPanel';
import { SettingsViewHeader } from '../settings/SettingsViewHeader';
import { useSettingsViewState } from '../../hooks/useSettingsViewState';

export const SettingsView: React.FC = () => {
  const state = useSettingsViewState();

  return (
    <div className="p-10 h-full overflow-y-auto bg-slate-950">
      <div className="max-w-3xl mx-auto">
        <SettingsViewHeader
          actionStatus={state.actionStatus}
          onNavigateToRuntimeConsole={state.navigateToRuntimeConsole}
          onNavigateToHelperCore={state.navigateToHelperCore}
          onCopyGovernanceSnapshot={state.copyGovernanceSnapshot}
          onExportGovernanceSnapshot={state.exportGovernanceSnapshot}
          onFocusSection={state.focusSettingsSection}
        />
        <div className="space-y-6">
          <section id="settings-alerts"><SettingsAlertsPanel items={state.settingsAlerts} /></section>
          <SettingsSecurityPanel status={state.status} />
          <section id="settings-infrastructure">
            <SettingsInfrastructurePanel isRefreshingRuntime={state.isRefreshingRuntime} infrastructureCards={state.infrastructureCards} routeTelemetry={state.routeTelemetry} />
          </section>
          <section id="settings-provider-profiles">
            <SettingsProviderProfilesPanel />
          </section>
          <section id="settings-runtime-doctor">
            <SettingsRuntimeDoctorPanel />
          </section>
          <section id="settings-capability-coverage">
            <CapabilityCoveragePanel snapshot={state.capabilityCatalog} error={state.capabilityError} isRefreshing={state.isRefreshingCapabilityCatalog} title="Capability Coverage" />
          </section>
          <section id="settings-conversation-quality">
            <HumanLikeConversationDashboardPanel snapshot={state.conversationQualityDashboard} error={state.conversationQualityError} isRefreshing={state.isRefreshingConversationQuality} title="Human-Like Conversation Dashboard" />
          </section>
          <SettingsConversationStylePanel
            responseStyle={state.responseStyle}
            preferredLanguage={state.preferredLanguage}
            warmth={state.warmth}
            enthusiasm={state.enthusiasm}
            directness={state.directness}
            defaultAnswerShape={state.defaultAnswerShape}
            stylePreview={state.stylePreview}
            onSaveStyle={state.saveStyle}
            onSaveLanguage={state.saveLanguage}
            onSaveWarmthPreference={state.saveWarmthPreference}
            onSaveEnthusiasmPreference={state.saveEnthusiasmPreference}
            onSaveDirectnessPreference={state.saveDirectnessPreference}
            onSaveDefaultAnswerShapePreference={state.saveDefaultAnswerShapePreference}
          />
          <section id="settings-personalization">
            <SettingsPersonalizationPanel
              decisionAssertiveness={state.decisionAssertiveness}
              clarificationTolerance={state.clarificationTolerance}
              citationPreference={state.citationPreference}
              repairStyle={state.repairStyle}
              reasoningStyle={state.reasoningStyle}
              reasoningEffort={state.reasoningEffort}
              onSaveDecisionAssertiveness={state.saveDecisionAssertiveness}
              onSaveClarificationTolerance={state.saveClarificationTolerance}
              onSaveCitationPreference={state.saveCitationPreference}
              onSaveRepairStyle={state.saveRepairStyle}
              onSaveReasoningStyle={state.saveReasoningStyle}
              onSaveReasoningEffort={state.saveReasoningEffort}
            />
          </section>
          <section id="settings-project-context">
            <SettingsProjectContextPanel
              projectId={state.projectId}
              projectLabel={state.projectLabel}
              projectInstructions={state.projectInstructions}
              projectMemoryEnabled={state.projectMemoryEnabled}
              backgroundResearchEnabled={state.backgroundResearchEnabled}
              proactiveUpdatesEnabled={state.proactiveUpdatesEnabled}
              referenceArtifacts={state.projectReferenceArtifacts}
              backgroundTasks={state.projectScopedBackgroundTasks}
              proactiveTopics={state.projectScopedProactiveTopics}
              onSetProjectId={state.setProjectId}
              onSetProjectLabel={state.setProjectLabel}
              onSetProjectInstructions={state.setProjectInstructions}
              onSetProjectMemoryEnabled={state.setProjectMemoryEnabled}
              onSetBackgroundResearchEnabled={state.setBackgroundResearchEnabled}
              onSetProactiveUpdatesEnabled={state.setProactiveUpdatesEnabled}
              onSaveProjectContext={state.saveProjectContext}
              onSaveContinuityControls={state.saveContinuityControls}
              onCancelBackgroundTask={(taskId) => {
                void state.cancelBackgroundTask(taskId);
              }}
              onSetProactiveTopicEnabled={(topicId, enabled) => {
                void state.setProactiveTopicEnabled(topicId, enabled);
              }}
            />
          </section>
          <section id="settings-memory-policy">
            <SettingsMemoryPolicyPanel
              memoryStatus={state.memoryStatus}
              isLoadingMemory={state.isLoadingMemory}
              memoryEnabled={state.memoryEnabled}
              personalConsent={state.personalConsent}
              sessionTtlMinutes={state.sessionTtlMinutes}
              taskTtlHours={state.taskTtlHours}
              longTermTtlDays={state.longTermTtlDays}
              isSavingPreferences={state.isSavingPreferences}
              onSetMemoryEnabled={state.setMemoryEnabled}
              onSetPersonalConsent={state.setPersonalConsent}
              onSetSessionTtlMinutes={state.setSessionTtlMinutes}
              onSetTaskTtlHours={state.setTaskTtlHours}
              onSetLongTermTtlDays={state.setLongTermTtlDays}
              onSavePreferences={(override) => {
                void state.savePreferences(override);
              }}
            />
          </section>
          <section id="settings-memory-items">
            <SettingsMemoryItemsPanel isLoadingMemory={state.isLoadingMemory} memoryItems={state.memoryItems} isDeletingMemory={state.isDeletingMemory} onDeleteMemoryItem={(memoryId) => {
              void state.deleteMemoryItem(memoryId);
            }} />
          </section>
        </div>
      </div>
    </div>
  );
};
