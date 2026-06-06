import { getDashboardStats, getActivePredictions, getPredictionHistory } from "@/lib/api";
import StatsGrid from "@/components/StatsGrid";
import PredictionsTable from "@/components/PredictionsTable";
import ControlPanel from "@/components/ControlPanel";
import Terminal from "@/components/Terminal";

export default async function DashboardPage() {
  const [stats, activePredictions, historyPredictions] = await Promise.all([
    getDashboardStats(),
    getActivePredictions(),
    getPredictionHistory(),
  ]);

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-8 px-4 py-8 sm:px-6 lg:px-8">
      {/* Header */}
      <header className="flex items-center justify-between border-b border-slate-800 pb-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-white">
            Quant Command Center
          </h1>
          <p className="mt-1 text-sm text-slate-400">
            Panel de control cuantitativo — rendimiento y predicciones en vivo
          </p>
        </div>
        <span className="hidden rounded-md bg-slate-800 px-3 py-1 text-xs font-mono text-slate-400 sm:inline-block">
          v0.2
        </span>
      </header>

      {/* Control panel */}
      <ControlPanel />

      {/* Dashboard stats (WinRate, Yield, ROI) */}
      <StatsGrid data={stats} />

      {/* Predictions table with tabs */}
      <PredictionsTable
        activePredictions={activePredictions}
        historyPredictions={historyPredictions}
      />

      {/* Live terminal (SignalR telemetry) */}
      <Terminal />
    </div>
  );
}
