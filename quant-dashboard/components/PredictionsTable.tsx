"use client";

import { useState, Fragment } from "react";
import type { PredictionDto } from "@/types/quant";
import { analyzeFailure } from "@/lib/api";

type Tab = "active" | "history";

function formatDate(iso: string): string {
  if (!iso) return "-";
  const d = new Date(iso);
  return d.toLocaleDateString("es-BO", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function estadoBadge(estado: string) {
  const base = "inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold";

  switch (estado) {
    case "Pendiente":
      return (
        <span className={`${base} bg-yellow-500/15 text-yellow-400`}>
          Pendiente
        </span>
      );
    case "Ganada":
      return (
        <span className={`${base} bg-emerald-500/15 text-emerald-400`}>
          Ganada
        </span>
      );
    case "Perdida":
      return (
        <span className={`${base} bg-red-500/15 text-red-400`}>
          Perdida
        </span>
      );
    default:
      return (
        <span className={`${base} bg-slate-500/15 text-slate-300`}>
          {estado}
        </span>
      );
  }
}

function mercadoBadge(mercado: string) {
  switch (mercado) {
    case "Corners":
      return <>🎯 Córners</>;
    case "Goles":
      return <>⚽ Goles</>;
    default:
      return <>🏆 Ganador</>;
  }
}

export default function PredictionsTable({
  activePredictions,
  historyPredictions,
}: {
  activePredictions: PredictionDto[];
  historyPredictions: PredictionDto[];
}) {
  const [tab, setTab] = useState<Tab>("active");
  const [expandedRowId, setExpandedRowId] = useState<string | null>(null);
  const [analyzingId, setAnalyzingId] = useState<string | null>(null);
  function toggleRow(id: string) {
    setExpandedRowId((prev) => (prev === id ? null : id));
  }

  async function handleAnalyzeFailure(predictionId: string) {
    setAnalyzingId(predictionId);
    try {
      const result = await analyzeFailure(predictionId);
      alert(`✅ ${result.message}`);
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Error desconocido";
      alert(`❌ ${msg}`);
    } finally {
      setAnalyzingId(null);
    }
  }

  const data = tab === "active" ? activePredictions : historyPredictions;

  const tabClass = (t: Tab) =>
    `rounded-lg px-4 py-2 text-sm font-semibold transition-colors ${
      tab === t
        ? "bg-emerald-600 text-white"
        : "bg-slate-800 text-slate-300 hover:bg-slate-700"
    }`;

  return (
    <section>
      {/* Tab buttons */}
      <div className="mb-3 flex items-center gap-2">
        <button onClick={() => setTab("active")} className={tabClass("active")}>
          Activas (En Juego)
        </button>
        <button onClick={() => setTab("history")} className={tabClass("history")}>
          Post-Mortem (Historial)
        </button>

        <span className="ml-auto text-xs text-slate-500">
          {data.length} registro{data.length !== 1 ? "s" : ""}
        </span>
      </div>

      <div className="overflow-x-auto rounded-xl border border-slate-700/60">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-700/60 bg-slate-900/80 text-xs font-medium uppercase tracking-wider text-slate-400">
              <th className="px-4 py-3">Fecha</th>
              <th className="px-4 py-3">Partido</th>
              <th className="px-4 py-3">Mercado</th>
              <th className="px-4 py-3">Selección</th>
              <th className="px-4 py-3 text-right">Cuota</th>
              <th className="px-4 py-3 text-right">Confianza</th>
              <th className="px-4 py-3 text-center">Estado</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800/80">
            {data.length === 0 ? (
              <tr>
                <td
                colSpan={7}
                  className="px-4 py-10 text-center text-slate-500"
                >
                  {tab === "active"
                    ? "No hay predicciones activas."
                    : "No hay historial de predicciones."}
                </td>
              </tr>
            ) : (
              data.map((p) => (
                <Fragment key={p.id}>
                  <tr
                    onClick={() => toggleRow(p.id)}
                    className="cursor-pointer transition-colors hover:bg-slate-800/40"
                  >
                    <td className="whitespace-nowrap px-4 py-3 text-slate-300">
                      {formatDate(p.inicio)}
                    </td>
                    <td className="px-4 py-3 font-medium text-white">
                      {p.local} vs {p.visitante}
                    </td>
                    <td className="px-4 py-3">
                      <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium
                        bg-slate-700/40 text-slate-300
                      ">
                        {mercadoBadge(p.mercado)}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-200">{p.seleccion}</td>
                    <td className="px-4 py-3 text-right tabular-nums text-slate-200">
                      {p.cuota.toFixed(3)}
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums text-slate-200">
                      {p.confianza}%
                    </td>
                    <td className="px-4 py-3 text-center">
                      {estadoBadge(p.estado)}
                    </td>
                  </tr>

                  {expandedRowId === p.id && (
                    <tr>
                      <td colSpan={7} className="px-4 pb-4 pt-1">
                        <div className="rounded-lg border border-slate-700/60 bg-slate-900 p-4">
                          <h4 className="mb-2 text-xs font-semibold uppercase tracking-wider text-slate-400">
                            Razonamiento de la IA
                          </h4>
                          <p className="whitespace-pre-wrap text-sm leading-relaxed text-slate-300">
                            {p.razonamiento || "Sin razonamiento disponible."}
                          </p>
                          {p.estado === "Perdida" && (
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                handleAnalyzeFailure(p.id);
                              }}
                              disabled={analyzingId === p.id}
                              className="mt-3 inline-flex items-center gap-1.5 rounded-md bg-red-600 px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-red-500 disabled:opacity-50"
                            >
                              {analyzingId === p.id ? "Analizando..." : "🔬 Forzar Autocrítica"}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  );
}
