"use client";

import type { KpiDto } from "@/types/quant";

function KpiCard({
  label,
  value,
  format,
  color,
}: {
  label: string;
  value: string | number;
  format?: "number" | "percent" | "odds" | "currency";
  color?: "green" | "red" | "white";
}) {
  let display: string;

  switch (format) {
    case "percent":
      display = `${value}%`;
      break;
    case "odds":
      display = Number(value).toFixed(3);
      break;
    case "currency":
      display = `${Number(value) >= 0 ? "+" : ""}${Number(value).toFixed(2)}u`;
      break;
    default:
      display = String(value);
  }

  const textColor =
    color === "green"
      ? "text-emerald-400"
      : color === "red"
        ? "text-red-400"
        : "text-white";

  return (
    <div className="flex flex-col gap-1 rounded-xl border border-slate-700/60 bg-slate-900/60 p-5 backdrop-blur-sm">
      <span className="text-xs font-medium uppercase tracking-widest text-slate-400">
        {label}
      </span>
      <span className={`text-2xl font-bold tabular-nums ${textColor}`}>
        {display}
      </span>
    </div>
  );
}

export default function KpiGrid({ data }: { data: KpiDto }) {
  const profitColor = data.netProfit >= 0 ? "green" : "red";

  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
      <KpiCard label="Total Bets" value={data.totalBets} format="number" />
      <KpiCard label="Win Rate" value={data.winRate} format="percent" />
      <KpiCard label="Average Odds" value={data.averageOdds} format="odds" />
      <KpiCard
        label="Net Profit"
        value={data.netProfit}
        format="currency"
        color={profitColor}
      />
    </div>
  );
}
