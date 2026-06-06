/* DTOs que reflejan los endpoints del backend .NET en /api */
export interface KpiDto {
  totalBets: number;
  pendingBets: number;
  resolvedBets: number;
  wins: number;
  losses: number;
  winRate: number;
  averageOdds: number;
  netProfit: number;
}

export interface PredictionDto {
  id: string;
  partidoId: string;
  local: string;
  visitante: string;
  inicio: string;
  marcadorLocal: number | null;
  marcadorVisitante: number | null;
  seleccion: string;
  cuota: number;
  confianza: number;
  razonamiento: string;
  estado: string;
  creado: string;
  actualizado: string;
  mercado: string;
  cornersOverUnder: number;
  totalGoals: number;
}

export interface DashboardStatsDto {
  totalPredictions: number;
  resolvedBets: number;
  pendingBets: number;
  wins: number;
  losses: number;
  winRate: number;
  yield: number;
  netProfitUnits: number;
  monthlyRoi: number;
  averageOdds: number;
  initialBankroll: number;
}

export interface SafetyStatusDto {
  status: string;
  dailyLoss: number;
  bankroll: number;
  threshold: number;
  consecutiveLosses: number;
  manuallyHalted: boolean;
  lastAlert: string | null;
}
