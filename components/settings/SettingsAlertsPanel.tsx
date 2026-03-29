import React from 'react';

type SettingsAlertsPanelProps = {
  runtimeError: string | null;
  capabilityError: string | null;
  conversationQualityError: string | null;
  memoryError: string | null;
};

export const SettingsAlertsPanel: React.FC<SettingsAlertsPanelProps> = ({
  runtimeError,
  capabilityError,
  conversationQualityError,
  memoryError,
}) => {
  if (!runtimeError && !capabilityError && !conversationQualityError && !memoryError) {
    return null;
  }

  return (
    <div className="bg-rose-950/30 p-4 rounded-xl border border-rose-900/50 text-sm text-rose-200 space-y-1">
      {runtimeError && <div>Runtime settings: {runtimeError}</div>}
      {capabilityError && <div>Capability catalog: {capabilityError}</div>}
      {conversationQualityError && <div>Conversation quality: {conversationQualityError}</div>}
      {memoryError && <div>Conversation memory: {memoryError}</div>}
    </div>
  );
};
