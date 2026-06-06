import type { DashboardStatsDto } from "@/types/quant";

function StatCard({
  label,
  value,
  formatter,
  accent,
}: {
  label: string;
  value: string | number;
  formatter?: (v: number) => string;
  accent?: "sky" | "lime" | "rose" | "white";
}) {
  const num =
    typeof value === "string" ? Number.parseFloat(value) : value;
  const display = formatter?.(num) ?? String(value);

  const borderColor =
    accent === "sky"
      ? "border-sky-500/30"
      : accent === "lime"
        ? "border-lime-500/30"
        : accent === "rose"
          ? "border-rose-500/30"
          : "border-slate-700/60";

  const textColor =
    accent === "sky"
      ? "text-sky-400"
      : accent === "lime"
        ? "text-lime-300"
        : accent === "rose"
          ? "text-rose-400"
          : "text-white";

  return (
    <div
      className={`flex flex-col gap-1 rounded-xl border bg-[#020617]/80 p-4 backdrop-blur-sm ${borderColor}`}
    >
      <span className="text-[10px] font-semibold uppercase tracking-[0.15em] text-slate-500">
        {label}
      </span>
      <span className={`text-xl font-bold tabular-nums ${textColor}`}>
        {display}
      </span>
    </div>
  );
}

export default function StatsGrid({ data }: { data: DashboardStatsDto }) {
  const pct = (v: number) => `${v >= 0 ? "+" : ""}${v.toFixed(2)}%`;
  const units = (v: number) =>
    `${v >= 0 ? "+" : ""}${v.toFixed(2)}u`;
  const ratio = (v: number) => `${v.toFixed(1)}%`;

  return (
    <div className="flex flex-col gap-3">
      <h2 className="text-sm font-semibold uppercase tracking-widest text-sky-400">
        Dashboard de Rendimiento
      </h2>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <StatCard
          label="Win Rate"
          value={data.winRate}
          formatter={ratio}
          accent="lime"
        />
        <StatCard
          label="Yield"
          value={data.yield}
          formatter={pct}
          accent={data.yield >= 0 ? "lime" : "rose"}
        />
        <StatCard
          label="ROI Mensual"
          value={data.monthlyRoi}
          formatter={pct}
          accent={data.monthlyRoi >= 0 ? "lime" : "rose"}
        />
        <StatCard
          label="Profit Neto"
          value={data.netProfitUnits}
          formatter={units}
          accent={data.netProfitUnits >= 0 ? "lime" : "rose"}
        />
        <StatCard
          label="Apuestas"
          value={`${data.wins}V / ${data.losses}D`}
          accent="white"
        />
        <StatCard
          label="Pendientes"
          value={data.pendingBets}
          accent="sky"
        />
        <StatCard
          label="Cuota Prom."
          value={data.averageOdds}
          formatter={(v) => v.toFixed(3)}
          accent="sky"
        />
        <StatCard
          label="Bankroll"
          value={data.initialBankroll}
          formatter={(v) => `${v.toFixed(0)}u`}
          accent="white"
        />
      </div>
    </div>
  );
}
