import type { KpiDto, PredictionDto, DashboardStatsDto } from "@/types/quant";

/*
 * SSR functions (getKpis, getActivePredictions, getPredictionHistory)
 * run on the server — use API_URL (internal Docker network or localhost).
 *
 * Client functions (forceIngestion) run in the browser —
 * use NEXT_PUBLIC_API_URL (Tailscale public URL or localhost).
 */

const SSR_BASE = process.env.API_URL ?? "http://localhost:5259/api";
const CLIENT_BASE =
  process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5259/api";

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error(`API error ${res.status}: ${res.statusText}`);
  }
  return res.json();
}

/* ---- KPIs (SSR) ---- */

export async function getKpis(): Promise<KpiDto> {
  try {
    return await fetchJson<KpiDto>(`${SSR_BASE}/analytics/kpis`);
  } catch {
    console.warn("[api] getKpis fallback: backend unreachable");
    return {
      totalBets: 0,
      pendingBets: 0,
      resolvedBets: 0,
      wins: 0,
      losses: 0,
      winRate: 0,
      averageOdds: 0,
      netProfit: 0,
    };
  }
}

/* ---- Predicciones activas (SSR) ---- */

export async function getActivePredictions(): Promise<PredictionDto[]> {
  try {
    return await fetchJson<PredictionDto[]>(`${SSR_BASE}/predictions/active`);
  } catch {
    console.warn("[api] getActivePredictions fallback: backend unreachable");
    return [];
  }
}

/* ---- Historial de predicciones resueltas (SSR) ---- */

export async function getPredictionHistory(): Promise<PredictionDto[]> {
  try {
    return await fetchJson<PredictionDto[]>(`${SSR_BASE}/predictions/history`);
  } catch {
    console.warn("[api] getPredictionHistory fallback: backend unreachable");
    return [];
  }
}

/* ---- Acciones (Client-side) ---- */

export async function forceIngestion(): Promise<{ message: string }> {
  const res = await fetch(`${CLIENT_BASE}/quant/ingest`, { method: "POST" });
  if (!res.ok) throw new Error(`Ingestion error ${res.status}`);
  return res.json();
}

export async function analyzeFailure(
  predictionId: string
): Promise<{ message: string; regla?: string }> {
  const res = await fetch(
    `${CLIENT_BASE}/quant/analyze-failure/${predictionId}`,
    { method: "POST" }
  );
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(body.message ?? `Error ${res.status}`);
  return body;
}

/* ---- Dashboard stats (SSR) ---- */

export async function getDashboardStats(): Promise<DashboardStatsDto> {
  try {
    return await fetchJson<DashboardStatsDto>(`${SSR_BASE}/stats/dashboard`);
  } catch {
    console.warn("[api] getDashboardStats fallback: backend unreachable");
    return {
      totalPredictions: 0,
      resolvedBets: 0,
      pendingBets: 0,
      wins: 0,
      losses: 0,
      winRate: 0,
      yield: 0,
      netProfitUnits: 0,
      monthlyRoi: 0,
      averageOdds: 0,
      initialBankroll: 1000,
    };
  }
}
