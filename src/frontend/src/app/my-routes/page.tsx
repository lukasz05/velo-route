'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useAuth, useClerk, useUser } from '@clerk/nextjs';
import type { SavedRouteSummary } from '@/types/route';

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
                  <span className="shrink-0 text-sm text-zinc-500">{formatDate(route.createdAt)}</span>
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
    </div>
  );
}
