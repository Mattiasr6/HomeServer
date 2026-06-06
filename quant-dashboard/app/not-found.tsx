import Link from "next/link";

export default function NotFoundPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-950 p-8">
      <div className="max-w-md text-center">
        <div className="mb-4 text-6xl font-bold text-slate-700">404</div>
        <h2 className="mb-2 text-xl font-bold text-slate-200">
          Página No Encontrada
        </h2>
        <p className="mb-6 text-sm text-slate-400">
          La página que buscas no existe en el panel de control.
        </p>
        <Link
          href="/"
          className="inline-block rounded-lg bg-sky-600 px-6 py-2 text-sm font-semibold text-white transition-colors hover:bg-sky-500"
        >
          Volver al Dashboard
        </Link>
      </div>
    </div>
  );
}
