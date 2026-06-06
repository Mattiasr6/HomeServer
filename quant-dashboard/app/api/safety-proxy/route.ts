import { NextResponse } from "next/server";

const API_URL = process.env.API_URL ?? "http://localhost:5259/api";

export async function GET() {
  try {
    const res = await fetch(`${API_URL}/stats/safety`, {
      next: { revalidate: 0 },
    });
    if (!res.ok) {
      return NextResponse.json(
        { status: "NORMAL", dailyLoss: 0, bankroll: 1000, threshold: 50, consecutiveLosses: 0, manuallyHalted: false, lastAlert: null },
        { status: 200 }
      );
    }
    const data = await res.json();
    return NextResponse.json(data);
  } catch {
    return NextResponse.json(
      { status: "NORMAL", dailyLoss: 0, bankroll: 1000, threshold: 50, consecutiveLosses: 0, manuallyHalted: false, lastAlert: null },
      { status: 200 }
    );
  }
}
