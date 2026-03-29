import React from 'react';
import { CapabilityCoveragePanel } from '../CapabilityCoveragePanel';
import { HumanLikeConversationDashboardPanel } from '../HumanLikeConversationDashboardPanel';
import { SettingsAlertsPanel } from '../settings/SettingsAlertsPanel';
import { SettingsSecurityPanel } from '../settings/SettingsSecurityPanel';
import { SettingsInfrastructurePanel } from '../settings/SettingsInfrastructurePanel';
import { SettingsConversationStylePanel } from '../settings/SettingsConversationStylePanel';
import { SettingsMemoryPolicyPanel } from '../settings/SettingsMemoryPolicyPanel';
import { SettingsMemoryItemsPanel } from '../settings/SettingsMemoryItemsPanel';
import { useSettingsViewState } from '../../hooks/useSettingsViewState';

export const SettingsView: React.FC = () => {
  const state = useSettingsViewState();

  return (
    <div className="p-10 h-full overflow-y-auto bg-slate-950">
      <div className="max-w-3xl mx-auto">
        <h2 className="text-3xl font-bold text-white mb-8 border-b border-slate-800 pb-4">System Settings</h2>

        <div className="space-y-6">
          <SettingsAlertsPanel
            runtimeError={state.runtimeError}
            capabilityError={state.capabilityError}
            conversationQualityError={state.conversationQualityError}
            memoryError={state.memoryError}
          />

          <SettingsSecurityPanel status={state.status} />

          <SettingsInfrastructurePanel
            isRefreshingRuntime={state.isRefreshingRuntime}
            infrastructureCards={state.infrastructureCards}
            routeTelemetry={state.routeTelemetry}
          />

          <CapabilityCoveragePanel
            snapshot={state.capabilityCatalog}
            error={state.capabilityError}
            isRefreshing={state.isRefreshingCapabilityCatalog}
            title="Capability Coverage"
          />

          <HumanLikeConversationDashboardPanel
            snapshot={state.conversationQualityDashboard}
            error={state.conversationQualityError}
            isRefreshing={state.isRefreshingConversationQuality}
            title="Human-Like Conversation Dashboard"
          />

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

          <SettingsMemoryItemsPanel
            isLoadingMemory={state.isLoadingMemory}
            memoryItems={state.memoryItems}
            isDeletingMemory={state.isDeletingMemory}
            onDeleteMemoryItem={(memoryId) => {
              void state.deleteMemoryItem(memoryId);
            }}
          />
        </div>
      </div>
    </div>
  );
};
