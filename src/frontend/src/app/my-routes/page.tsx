'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useAuth, useClerk, useUser } from '@clerk/nextjs';
import type { SavedRouteSummary } from '@/types/route';
import ConfirmModal from '@/components/ConfirmModal';

function formatDate(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export default function MyRoutesPage() {
  const { isLoaded, isSignedIn } = useUser();
  const { getToken } = useAuth();
  const { openSignIn } = useClerk();
  const router = useRouter();

  const [routes, setRoutes] = useState<SavedRouteSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [routeToDelete, setRouteToDelete] = useState<SavedRouteSummary | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  useEffect(() => {
    if (!isLoaded) return;
    if (!isSignedIn) {
      router.replace('/');
      openSignIn();
      return;
    }

    (async () => {
      setError(null);
      try {
        const token = await getToken();
        const res = await fetch('/api/routes', {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!res.ok) throw new Error(`Failed to load routes: ${res.status}`);
        const data = await res.json() as SavedRouteSummary[];
        setRoutes(data);
      } catch {
        setError('Could not load your routes. Please try again.');
      }
    })();
  }, [isLoaded, isSignedIn, getToken, openSignIn, router]);

  async function handleConfirmDelete() {
    if (!routeToDelete) return;
    setDeletingId(routeToDelete.id);
    setDeleteError(null);
    try {
      const token = await getToken();
      const res = await fetch(`/api/routes/${routeToDelete.id}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.status === 204 || res.status === 404) {
        setRoutes((prev) => prev?.filter((r) => r.id !== routeToDelete.id) ?? prev);
        setRouteToDelete(null);
        return;
      }
      throw new Error(`Delete failed: ${res.status}`);
    } catch {
      setDeleteError('Could not delete this route. Please try again.');
      setRouteToDelete(null);
    } finally {
      setDeletingId(null);
    }
  }

  if (!isLoaded || !isSignedIn) return null;

  if (!error && routes !== null && routes.length === 0) {
    return (
      <div className="max-w-3xl p-6">
        <h1 className="text-xl font-bold text-zinc-900">My Routes</h1>
        <div className="mt-4 flex flex-col items-start gap-2 rounded-lg border border-dashed border-zinc-300 p-8">
          <p className="text-sm text-zinc-500">No saved routes yet.</p>
          <Link href="/" className="text-sm font-medium text-blue-600 hover:underline">
            Plan a route
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-3xl p-6">
      <h1 className="text-xl font-bold text-zinc-900">My Routes</h1>

      {error && <p className="mt-4 text-sm text-red-600">{error}</p>}
      {deleteError && <p className="mt-4 text-sm text-red-600">{deleteError}</p>}

      {!error && routes === null && (
        <p className="mt-4 text-sm text-zinc-500">Loading your routes…</p>
      )}

      {!error && routes !== null && routes.length > 0 && (
        <ul className="mt-4 flex flex-col gap-3">
          {routes.map((route) => (
            <li key={route.id}>
              <Link
                href={`/my-routes/${route.id}`}
                className="flex flex-col gap-2 rounded-lg border border-zinc-200 p-5 hover:border-zinc-300 hover:bg-zinc-50"
              >
                <div className="flex items-baseline justify-between gap-4">
                  <span className="text-base font-medium text-zinc-900">{route.name}</span>
                  <div className="flex shrink-0 items-baseline gap-3">
                    <span className="text-sm text-zinc-500">{formatDate(route.createdAt)}</span>
                    <button
                      onClick={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        setRouteToDelete(route);
                      }}
                      disabled={deletingId === route.id}
                      aria-label={`Delete ${route.name}`}
                      className="rounded px-1.5 py-0.5 text-xs font-medium text-zinc-400 hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                      {deletingId === route.id ? 'Deleting…' : 'Delete'}
                    </button>
                  </div>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-sm font-medium text-zinc-700">{route.distanceKm.toFixed(1)} km</span>
                  {route.tags?.map((tag) => (
                    <span
                      key={tag}
                      className="rounded-full bg-zinc-100 px-2 py-0.5 text-xs text-zinc-600"
                    >
                      {tag}
                    </span>
                  ))}
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}

      {routeToDelete && (
        <ConfirmModal
          title="Delete route?"
          message={`Delete "${routeToDelete.name}"? This cannot be undone.`}
          confirmLabel="Delete"
          isConfirming={deletingId === routeToDelete.id}
          onConfirm={handleConfirmDelete}
          onCancel={() => setRouteToDelete(null)}
        />
      )}
    </div>
  );
}
