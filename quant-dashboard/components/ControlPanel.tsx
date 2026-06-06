"use client";

import { useState, useEffect, useCallback } from "react";
import { forceIngestion } from "@/lib/api";
import type { SafetyStatusDto } from "@/types/quant";

interface ControlPanelProps {
  initialSafety?: SafetyStatusDto;
}

export default function ControlPanel({ initialSafety }: ControlPanelProps) {
  const [loading, setLoading] = useState(false);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [safety, setSafety] = useState<SafetyStatusDto | null>(initialSafety ?? null);

  // Poll safety status every 30 seconds
  const fetchSafety = useCallback(async () => {
    try {
      const res = await fetch("/api/safety-proxy");
      if (res.ok) {
        const data: SafetyStatusDto = await res.json();
        setSafety(data);
      }
    } catch {
      // silent — don't spam logs on transient network blips
    }
  }, []);

  useEffect(() => {
    fetchSafety();
    const interval = setInterval(fetchSafety, 30_000);
    return () => clearInterval(interval);
  }, [fetchSafety]);

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

  const isHalted = safety?.status === "EMERGENCY_HALT" || safety?.manuallyHalted;
  const lossPct = safety && safety.bankroll > 0
    ? ((safety.dailyLoss / safety.bankroll) * 100).toFixed(1)
    : "0.0";

  return (
    <div className="flex flex-col gap-3">
      {/* Safety banner */}
      {safety && (
        <div
          className={`flex flex-wrap items-center gap-3 rounded-xl border p-3 text-sm backdrop-blur-sm $
            isHalted
              ? "border-red-700/60 bg-red-950/50 text-red-300"
              : "border-emerald-700/60 bg-emerald-950/50 text-emerald-300"
          }`}>
          <span className="font-semibold">
            {isHalted ? "🚨 EMERGENCY HALT" : "✅ Sistema Normal"}
          </span>
          <span className="text-slate-400">|</span>
          <span>
            Pérdida: ${safety.dailyLoss.toFixed(2)} / ${safety.threshold.toFixed(2)}
            ({lossPct}% del bankroll)
          </span>
          <span className="text-slate-400">|</span>
          <span>
            Rachas: {safety.consecutiveLosses} consecutivas
          </span>
          {safety.manuallyHalted && (
            <>
              <span className="text-slate-400">|</span>
              <span className="text-amber-400">🛑 Detenido manualmente</span>
            </>
          )}
        </div>
      )}

      {/* Ingestion controls */}
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
    </div>
  );
}
