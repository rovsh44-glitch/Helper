import React, { useEffect, useRef } from 'react';

interface InlineActionSheetProps {
  title: string;
  description: string;
  submitLabel: string;
  cancelLabel?: string;
  submitTone?: 'primary' | 'danger';
  disabled?: boolean;
  submitDisabled?: boolean;
  children: React.ReactNode;
  onSubmit: () => void;
  onClose: () => void;
}

export function InlineActionSheet({
  title,
  description,
  submitLabel,
  cancelLabel = 'Cancel',
  submitTone = 'primary',
  disabled = false,
  submitDisabled = false,
  children,
  onSubmit,
  onClose,
}: InlineActionSheetProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    containerRef.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, []);

  const submitClassName = submitTone === 'danger'
    ? 'rounded-xl border border-rose-700 bg-rose-500/10 px-4 py-2 text-xs font-semibold uppercase tracking-wide text-rose-100 transition-colors hover:border-rose-500 hover:bg-rose-500/15 disabled:opacity-50'
    : 'rounded-xl border border-primary-600 bg-primary-500/10 px-4 py-2 text-xs font-semibold uppercase tracking-wide text-primary-100 transition-colors hover:border-primary-500 hover:bg-primary-500/15 disabled:opacity-50';

  return (
    <div
      ref={containerRef}
      className="rounded-2xl border border-slate-800 bg-black/30 p-4 shadow-[0_18px_60px_rgba(0,0,0,0.24)]"
    >
      <div className="flex items-start justify-between gap-4">
        <div>
          <div className="text-[11px] uppercase tracking-[0.22em] text-slate-400">{title}</div>
          <div className="mt-1 text-sm text-slate-200">{description}</div>
        </div>
        <button
          onClick={onClose}
          disabled={disabled}
          className="rounded-full border border-slate-700 px-3 py-1 text-[11px] text-slate-300 hover:border-slate-500 disabled:opacity-50"
        >
          {cancelLabel}
        </button>
      </div>

      <div className="mt-4">{children}</div>

      <div className="mt-4 flex items-center justify-end gap-2">
        <button
          onClick={onSubmit}
          disabled={disabled || submitDisabled}
          className={submitClassName}
        >
          {submitLabel}
        </button>
      </div>
    </div>
  );
}
