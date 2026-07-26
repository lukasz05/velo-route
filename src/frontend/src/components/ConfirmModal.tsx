'use client';

import { useEffect, useState } from 'react';

export default function ConfirmModal({
  title,
  message,
  confirmLabel,
  onConfirm,
  onCancel,
  isConfirming = false,
  confirmationPhrase,
}: {
  title: string;
  message: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  isConfirming?: boolean;
  confirmationPhrase?: string;
}) {
  const [typedValue, setTypedValue] = useState('');

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onCancel();
    }
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onCancel]);

  const confirmDisabled =
    isConfirming || (confirmationPhrase !== undefined && typedValue !== confirmationPhrase);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div role="dialog" aria-modal="true" aria-labelledby="confirm-modal-title" className="w-full max-w-sm rounded-lg bg-white p-6 shadow-xl">
        <h2 id="confirm-modal-title" className="text-lg font-semibold text-zinc-900">{title}</h2>
        <p className="mt-2 text-sm text-zinc-600">{message}</p>
        {confirmationPhrase !== undefined && (
          <input
            type="text"
            value={typedValue}
            onChange={(e) => setTypedValue(e.target.value)}
            placeholder={`Type ${confirmationPhrase} to confirm`}
            aria-label={`Type ${confirmationPhrase} to confirm`}
            disabled={isConfirming}
            className="mt-4 w-full rounded-md border border-zinc-300 px-3 py-2 text-sm disabled:cursor-not-allowed disabled:opacity-50"
          />
        )}
        <div className="mt-6 flex justify-end gap-3">
          <button
            onClick={onCancel}
            disabled={isConfirming}
            className="rounded-md border border-zinc-300 bg-white px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={confirmDisabled}
            className="rounded-md border border-red-300 bg-white px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isConfirming ? 'Deleting…' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
