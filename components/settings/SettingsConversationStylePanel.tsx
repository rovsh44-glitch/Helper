import React from 'react';
import type { ConversationStylePreviewModel } from '../../utils/conversationStylePreview';

type SettingsConversationStylePanelProps = {
  responseStyle: string;
  preferredLanguage: string;
  warmth: string;
  enthusiasm: string;
  directness: string;
  defaultAnswerShape: string;
  stylePreview: ConversationStylePreviewModel;
  onSaveStyle: (value: string) => void;
  onSaveLanguage: (value: string) => void;
  onSaveWarmthPreference: (value: string) => void;
  onSaveEnthusiasmPreference: (value: string) => void;
  onSaveDirectnessPreference: (value: string) => void;
  onSaveDefaultAnswerShapePreference: (value: string) => void;
};

export const SettingsConversationStylePanel: React.FC<SettingsConversationStylePanelProps> = ({
  responseStyle,
  preferredLanguage,
  warmth,
  enthusiasm,
  directness,
  defaultAnswerShape,
  stylePreview,
  onSaveStyle,
  onSaveLanguage,
  onSaveWarmthPreference,
  onSaveEnthusiasmPreference,
  onSaveDirectnessPreference,
  onSaveDefaultAnswerShapePreference,
}) => (
  <div className="bg-slate-900 p-6 rounded-xl border border-slate-800">
    <h3 className="text-sm font-bold text-primary-400 uppercase mb-4">Conversation Style</h3>
    <p className="text-[11px] text-slate-500 mb-4">
      These controls persist per conversation and feed the runtime style route, system hint, and default answer shaping.
    </p>
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      <div className="space-y-2">
        <label className="text-xs text-slate-500 uppercase">Preferred Response Style</label>
        <select
          value={responseStyle}
          onChange={(e) => onSaveStyle(e.target.value)}
          className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200"
        >
          <option value="balanced">Balanced</option>
          <option value="concise">Concise</option>
          <option value="detailed">Detailed</option>
        </select>
      </div>
      <div className="space-y-2">
        <label className="text-xs text-slate-500 uppercase">Preferred Language</label>
        <select
          value={preferredLanguage}
          onChange={(e) => onSaveLanguage(e.target.value)}
          className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200"
        >
          <option value="auto">Auto</option>
          <option value="ru">Russian</option>
          <option value="en">English</option>
        </select>
      </div>
      <div className="space-y-2">
        <label className="text-xs text-slate-500 uppercase">Warmth</label>
        <select
          value={warmth}
          onChange={(e) => onSaveWarmthPreference(e.target.value)}
          className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200"
        >
          <option value="balanced">Balanced</option>
          <option value="cool">Cool</option>
          <option value="warm">Warm</option>
        </select>
      </div>
      <div className="space-y-2">
        <label className="text-xs text-slate-500 uppercase">Enthusiasm</label>
        <select
          value={enthusiasm}
          onChange={(e) => onSaveEnthusiasmPreference(e.target.value)}
          className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200"
        >
          <option value="balanced">Balanced</option>
          <option value="low">Low</option>
          <option value="high">High</option>
        </select>
      </div>
      <div className="space-y-2">
        <label className="text-xs text-slate-500 uppercase">Directness</label>
        <select
          value={directness}
          onChange={(e) => onSaveDirectnessPreference(e.target.value)}
          className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200"
        >
          <option value="balanced">Balanced</option>
          <option value="soft">Soft</option>
          <option value="direct">Direct</option>
        </select>
      </div>
      <div className="space-y-2">
        <label className="text-xs text-slate-500 uppercase">Default Answer Shape</label>
        <select
          value={defaultAnswerShape}
          onChange={(e) => onSaveDefaultAnswerShapePreference(e.target.value)}
          className="w-full bg-black/40 border border-slate-800 rounded px-3 py-2 text-xs text-slate-200"
        >
          <option value="auto">Auto</option>
          <option value="paragraph">Paragraph</option>
          <option value="bullets">Bullets</option>
        </select>
      </div>
    </div>
    <div className="mt-5 rounded-xl border border-amber-500/20 bg-amber-500/5 p-4">
      <div className="text-[10px] uppercase tracking-[0.18em] text-amber-200/80">Style Preview</div>
      <div className="mt-3 flex flex-wrap gap-2">
        {stylePreview.summary.map(item => (
          <span key={item} className="rounded-full border border-white/10 bg-black/20 px-2 py-1 text-[10px] text-slate-300">
            {item}
          </span>
        ))}
      </div>
      <div className="mt-4 rounded-lg border border-white/5 bg-black/20 px-4 py-3 text-sm leading-6 text-slate-200 whitespace-pre-line">
        {stylePreview.preview}
      </div>
      <div className="mt-3 text-[10px] text-slate-500">
        Preview is local and illustrative; the persisted controls are also sent to the backend style route and system hint.
      </div>
    </div>
  </div>
);
