import React, { useEffect, useRef } from 'react';
import type {
  RepairConversationDraft,
  RepairQuickActionId,
  RepairQuickActionPreset,
  RepairUiLanguage,
} from '../../services/conversationApi';

interface RepairIntentSheetProps {
  language: RepairUiLanguage;
  messagePreview: string;
  draft: RepairConversationDraft;
  quickActions: RepairQuickActionPreset[];
  disabled: boolean;
  onSelectQuickAction: (actionId: RepairQuickActionId) => void;
  onChangeCorrectedIntent: (value: string) => void;
  onChangeRepairNote: (value: string) => void;
  onSubmit: () => void;
  onClose: () => void;
}

export function RepairIntentSheet({
  language,
  messagePreview,
  draft,
  quickActions,
  disabled,
  onSelectQuickAction,
  onChangeCorrectedIntent,
  onChangeRepairNote,
  onSubmit,
  onClose,
}: RepairIntentSheetProps) {
  const correctedIntentRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    correctedIntentRef.current?.focus();
  }, []);

  const copy = language === 'ru'
    ? {
        title: 'Уточнить смысл ответа',
        subtitle: 'Быстро скорректируйте трактовку или задайте точное направление для repair-flow.',
        original: 'Фрагмент ответа',
        correctedIntent: 'Что нужно исправить',
        correctedIntentPlaceholder: 'Опишите, как именно нужно перестроить ответ.',
        repairNote: 'Дополнительная рамка',
        repairNotePlaceholder: 'Например: короче, без воды, с примерами, в markdown, списком шагов.',
        cancel: 'Отмена',
        submit: 'Перестроить ответ',
      }
    : {
        title: 'Repair The Response',
        subtitle: 'Correct the interpretation quickly or give the repair flow a clearer direction.',
        original: 'Response excerpt',
        correctedIntent: 'What to correct',
        correctedIntentPlaceholder: 'Describe how the answer should be reframed.',
        repairNote: 'Optional constraints',
        repairNotePlaceholder: 'For example: shorter, no fluff, add examples, markdown, checklist.',
        cancel: 'Cancel',
        submit: 'Apply Repair',
      };

  return (
    <div className="mt-3 rounded-2xl border border-amber-800/70 bg-amber-950/10 p-4 shadow-[0_20px_60px_rgba(0,0,0,0.28)]">
      <div className="flex items-start justify-between gap-4">
        <div>
          <div className="text-[11px] uppercase tracking-[0.24em] text-amber-300/80">{copy.title}</div>
          <div className="mt-1 text-sm text-slate-200">{copy.subtitle}</div>
        </div>
        <button
          onClick={onClose}
          disabled={disabled}
          className="rounded-full border border-slate-700 px-3 py-1 text-[11px] text-slate-300 hover:border-slate-500 disabled:opacity-50"
        >
          {copy.cancel}
        </button>
      </div>

      <div className="mt-4 rounded-xl border border-slate-800 bg-black/30 p-3">
        <div className="text-[10px] uppercase tracking-wide text-slate-500">{copy.original}</div>
        <div className="mt-2 text-sm leading-relaxed text-slate-300">
          {messagePreview}
        </div>
      </div>

      <div className="mt-4 flex flex-wrap gap-2">
        {quickActions.map(action => (
          <button
            key={action.id}
            onClick={() => onSelectQuickAction(action.id)}
            disabled={disabled}
            className={`rounded-full border px-3 py-1.5 text-[11px] transition-colors ${
              draft.selectedActionId === action.id
                ? 'border-amber-400 bg-amber-400/10 text-amber-200'
                : 'border-slate-700 text-slate-300 hover:border-amber-600 hover:text-amber-200'
            } disabled:opacity-50`}
            title={action.description}
          >
            {action.label}
          </button>
        ))}
      </div>

      <div className="mt-4 grid gap-3">
        <label className="grid gap-1.5">
          <span className="text-[11px] uppercase tracking-wide text-slate-500">{copy.correctedIntent}</span>
          <textarea
            ref={correctedIntentRef}
            value={draft.correctedIntent}
            onChange={(event) => onChangeCorrectedIntent(event.target.value)}
            rows={3}
            disabled={disabled}
            placeholder={copy.correctedIntentPlaceholder}
            className="resize-y rounded-xl border border-slate-800 bg-slate-950 px-3 py-2 text-sm leading-relaxed text-slate-200 outline-none transition-colors focus:border-amber-500 disabled:opacity-50"
          />
        </label>

        <label className="grid gap-1.5">
          <span className="text-[11px] uppercase tracking-wide text-slate-500">{copy.repairNote}</span>
          <textarea
            value={draft.repairNote}
            onChange={(event) => onChangeRepairNote(event.target.value)}
            rows={2}
            disabled={disabled}
            placeholder={copy.repairNotePlaceholder}
            className="resize-y rounded-xl border border-slate-800 bg-slate-950 px-3 py-2 text-sm leading-relaxed text-slate-200 outline-none transition-colors focus:border-amber-500 disabled:opacity-50"
          />
        </label>
      </div>

      <div className="mt-4 flex items-center justify-end gap-2">
        <button
          onClick={onSubmit}
          disabled={disabled || !draft.correctedIntent.trim()}
          className="rounded-xl border border-amber-700 bg-amber-500/10 px-4 py-2 text-xs font-semibold uppercase tracking-wide text-amber-200 transition-colors hover:border-amber-500 hover:bg-amber-500/15 disabled:opacity-50"
        >
          {copy.submit}
        </button>
      </div>
    </div>
  );
}
