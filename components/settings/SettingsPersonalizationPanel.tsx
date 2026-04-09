import React from 'react';

type SettingsPersonalizationPanelProps = {
  decisionAssertiveness: string;
  clarificationTolerance: string;
  citationPreference: string;
  repairStyle: string;
  reasoningStyle: string;
  reasoningEffort: string;
  onSaveDecisionAssertiveness: (value: string) => void;
  onSaveClarificationTolerance: (value: string) => void;
  onSaveCitationPreference: (value: string) => void;
  onSaveRepairStyle: (value: string) => void;
  onSaveReasoningStyle: (value: string) => void;
  onSaveReasoningEffort: (value: string) => void;
};

export const SettingsPersonalizationPanel: React.FC<SettingsPersonalizationPanelProps> = ({
  decisionAssertiveness,
  clarificationTolerance,
  citationPreference,
  repairStyle,
  reasoningStyle,
  reasoningEffort,
  onSaveDecisionAssertiveness,
  onSaveClarificationTolerance,
  onSaveCitationPreference,
  onSaveRepairStyle,
  onSaveReasoningStyle,
  onSaveReasoningEffort,
}) => (
  <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
    <h3 className="text-sm font-bold text-primary-400 uppercase mb-4">Personalization</h3>
    <p className="text-[11px] text-slate-500 mb-4">
      These controls shape collaboration style above the basic warmth/directness layer and are meant to stay inspectable and reversible.
    </p>
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      <label className="space-y-2">
        <span className="text-xs text-slate-500 uppercase">Decision Assertiveness</span>
        <select value={decisionAssertiveness} onChange={(e) => onSaveDecisionAssertiveness(e.target.value)} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200">
          <option value="balanced">Balanced</option>
          <option value="low">Cautious</option>
          <option value="high">Decisive</option>
        </select>
      </label>
      <label className="space-y-2">
        <span className="text-xs text-slate-500 uppercase">Clarification Tolerance</span>
        <select value={clarificationTolerance} onChange={(e) => onSaveClarificationTolerance(e.target.value)} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200">
          <option value="balanced">Balanced</option>
          <option value="low">Low</option>
          <option value="high">High</option>
        </select>
      </label>
      <label className="space-y-2">
        <span className="text-xs text-slate-500 uppercase">Citation Preference</span>
        <select value={citationPreference} onChange={(e) => onSaveCitationPreference(e.target.value)} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200">
          <option value="adaptive">Adaptive</option>
          <option value="prefer">Prefer citations</option>
          <option value="avoid">Avoid unless needed</option>
        </select>
      </label>
      <label className="space-y-2">
        <span className="text-xs text-slate-500 uppercase">Repair Style</span>
        <select value={repairStyle} onChange={(e) => onSaveRepairStyle(e.target.value)} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200">
          <option value="direct_fix">Direct fix</option>
          <option value="explain_first">Explain first</option>
          <option value="gentle_reset">Gentle reset</option>
        </select>
      </label>
      <label className="space-y-2">
        <span className="text-xs text-slate-500 uppercase">Reasoning Style</span>
        <select value={reasoningStyle} onChange={(e) => onSaveReasoningStyle(e.target.value)} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200">
          <option value="concise">Concise</option>
          <option value="exploratory">Exploratory</option>
        </select>
      </label>
      <label className="space-y-2">
        <span className="text-xs text-slate-500 uppercase">Reasoning Effort</span>
        <select value={reasoningEffort} onChange={(e) => onSaveReasoningEffort(e.target.value)} className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200">
          <option value="fast">Fast</option>
          <option value="balanced">Balanced</option>
          <option value="deep">Deep</option>
        </select>
      </label>
    </div>
  </div>
);
