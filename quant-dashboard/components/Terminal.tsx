"use client";

import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";

interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
}

const LEVEL_COLORS: Record<string, string> = {
  INFO: "text-cyan-300",
  AI: "text-lime-300",
  ERROR: "text-rose-400",
};

const LEVEL_BADGE: Record<string, string> = {
  INFO: "bg-cyan-600/30 text-cyan-300 border-cyan-700/50",
  AI: "bg-lime-600/30 text-lime-300 border-lime-700/50",
  ERROR: "bg-rose-600/30 text-rose-300 border-rose-700/50",
};

// Derive SignalR hub URL from NEXT_PUBLIC_API_URL, stripping any /api suffix
const API_ORIGIN =
  (process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5259").replace(/\/api$/, "");
const HUB_URL = `${API_ORIGIN}/hubs/logging`;

export default function Terminal() {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const bottomRef = useRef<HTMLDivElement>(null);
  const [connected, setConnected] = useState(false);
  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on("OnLog", (entry: LogEntry) => {
      setLogs((prev) => {
        const next = [...prev, entry];
        return next.length > 200 ? next.slice(-200) : next;
      });
    });

    connection.start()
      .then(() => setConnected(true))
      .catch((err) => {
        console.warn("[Terminal] SignalR connection failed:", err);
      });

    return () => {
      connection.stop();
    };
  }, []);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [logs]);

  const timeStr = (iso: string) => {
    const d = new Date(iso);
    return d.toLocaleTimeString("es-BO", { hour12: false });
  };

  return (
    <div className="flex flex-col gap-2">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold uppercase tracking-widest text-sky-400">
          Terminal en vivo
        </h2>
        <span
          className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[10px] font-medium uppercase tracking-wider ${
            connected
              ? "bg-emerald-600/20 text-emerald-400"
              : "bg-rose-600/20 text-rose-400"
          }`}
        >
          <span
            className={`h-1.5 w-1.5 rounded-full ${
              connected ? "bg-emerald-400" : "bg-rose-400"
            }`}
          />
          {connected ? "Conectado" : "Desconectado"}
        </span>
      </div>

      {/* Log area */}
      <div className="max-h-96 overflow-y-auto rounded-xl border border-slate-700/60 bg-[#020617] p-3 font-mono text-[13px] leading-relaxed scrollbar-thin scrollbar-track-slate-800 scrollbar-thumb-slate-600">
        {logs.length === 0 && (
          <p className="select-none text-slate-600">
            Esperando logs del pipeline...
          </p>
        )}

        {logs.map((entry, i) => (
          <div key={i} className="flex items-start gap-2 py-px">
            {/* Timestamp */}
            <span className="shrink-0 text-slate-500">
              {timeStr(entry.timestamp)}
            </span>

            {/* Level badge */}
            <span
              className={`shrink-0 rounded border px-1.5 py-px text-[10px] font-semibold uppercase leading-none ${
                LEVEL_BADGE[entry.level] ?? "bg-slate-700 text-slate-300"
              }`}
            >
              {entry.level}
            </span>

            {/* Message */}
            <span
              className={
                LEVEL_COLORS[entry.level] ?? "text-slate-200"
              }
            >
              {entry.message}
            </span>
          </div>
        ))}

        <div ref={bottomRef} />
      </div>
    </div>
  );
}
