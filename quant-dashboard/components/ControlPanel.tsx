"use client";

import { useState } from "react";
import { forceIngestion } from "@/lib/api";

export default function ControlPanel() {
  const [loading, setLoading] = useState(false);
  const [feedback, setFeedback] = useState<string | null>(null);

  async function handleIngest() {
    setLoading(true);
    setFeedback(null);
    try {
      const res = await forceIngestion();
      setFeedback(res.message);
    } catch {
      setFeedback("Error: no se pudo conectar con el backend.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex flex-wrap items-center gap-4 rounded-xl border border-slate-700/60 bg-slate-900/50 p-4 backdrop-blur-sm">
      <span className="text-sm font-medium text-slate-300">Ignición</span>

      <button
        onClick={handleIngest}
        disabled={loading}
        className="inline-flex items-center gap-2 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-emerald-500 disabled:cursor-wait disabled:opacity-60"
      >
        {loading ? (
          <>
            <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />
            Cazando...
          </>
        ) : (
          "⚡ Forzar Ingesta Diaria"
        )}
      </button>

      {feedback && (
        <span className="text-sm text-slate-400">{feedback}</span>
      )}
    </div>
  );
}
