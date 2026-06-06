"use client";

export default function ErrorPage({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-950 p-8">
      <div className="max-w-md rounded-xl border border-red-700/60 bg-red-950/30 p-8 text-center backdrop-blur-sm">
        <div className="mb-4 text-4xl">💥</div>
        <h2 className="mb-2 text-xl font-bold text-red-300">
          Error Crítico
        </h2>
        <p className="mb-6 text-sm text-slate-400">
          El panel de control encontró un error inesperado.
          {error.digest && (
            <span className="mt-2 block text-xs text-slate-500">
              Código: {error.digest}
            </span>
          )}
        </p>
        <button
          onClick={reset}
          className="rounded-lg bg-red-600 px-6 py-2 text-sm font-semibold text-white transition-colors hover:bg-red-500"
        >
          Reintentar
        </button>
      </div>
    </div>
  );
}
