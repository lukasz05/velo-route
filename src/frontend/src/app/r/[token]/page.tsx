'use client';

import { useEffect, useState } from 'react';
import dynamic from 'next/dynamic';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import type { SavedRouteDetail } from '@/types/route';

const RouteMap = dynamic(() => import('@/components/RouteMap'), { ssr: false });

function formatTimestamp(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}T${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

export default function PublicSharePage() {
  const { token } = useParams<{ token: string }>();

  const [route, setRoute] = useState<SavedRouteDetail | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      setError(null);
      setNotFound(false);
      try {
        const res = await fetch(`/api/shares/${token}`);
        if (res.status === 404) {
          setNotFound(true);
          return;
        }
        if (!res.ok) throw new Error(`Failed to load route: ${res.status}`);
        const data = await res.json() as SavedRouteDetail;
        setRoute(data);
      } catch {
        setError('Could not load this route. Please try again.');
      }
    })();
  }, [token]);

  async function handleDownload() {
    if (!route) return;
    setIsDownloading(true);
    setDownloadError(null);
    try {
      const res = await fetch('/api/routes/gpx', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ coordinates: route.geometry.coordinates }),
      });
      if (!res.ok) throw new Error(`GPX export failed: ${res.status}`);
      const gpxText = await res.text();
      const blob = new Blob([gpxText], { type: 'application/gpx+xml' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `veloroute-${formatTimestamp(new Date())}.gpx`;
      document.body.appendChild(a);
      try {
        a.click();
      } finally {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
      }
    } catch {
      setDownloadError('GPX export failed. Please try again.');
    } finally {
      setIsDownloading(false);
    }
  }

  if (notFound) {
    return (
      <div className="mx-auto max-w-2xl p-6">
        <p className="text-sm text-zinc-500">Route not found.</p>
        <Link href="/" className="mt-2 inline-block text-sm font-medium text-blue-600 hover:underline">
          Back to VeloRoute
        </Link>
      </div>
    );
  }

  if (error) {
    return (
      <div className="mx-auto max-w-2xl p-6">
        <p className="text-sm text-red-600">{error}</p>
      </div>
    );
  }

  if (!route) {
    return (
      <div className="mx-auto max-w-2xl p-6">
        <p className="text-sm text-zinc-500">Loading route…</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col md:h-screen md:flex-row">
      <div className="flex w-full flex-col gap-2 bg-white p-6 md:w-96 md:overflow-y-auto md:border-r md:border-zinc-200">
        <h1 className="text-xl font-bold text-zinc-900">{route.name}</h1>
        <p className="text-sm text-zinc-500">Total distance</p>
        <p className="text-2xl font-semibold text-zinc-900">{route.distanceKm.toFixed(1)} km</p>
        {route.tags && route.tags.length > 0 && (
          <p className="text-sm text-zinc-500">Tags: {route.tags.join(', ')}</p>
        )}
        <button
          onClick={handleDownload}
          disabled={isDownloading}
          className="mt-3 w-full rounded-md border border-zinc-300 bg-white px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {isDownloading ? 'Downloading…' : 'Download GPX'}
        </button>
        {downloadError && (
          <p className="mt-2 text-sm text-red-600">{downloadError}</p>
        )}
        <Link href="/" className="mt-2 text-sm font-medium text-blue-600 hover:underline">
          Plan your own route
        </Link>
      </div>

      <div className="h-[60vh] md:h-full md:flex-1">
        <RouteMap startPoint={null} routeCoordinates={route.geometry.coordinates} />
      </div>
    </div>
  );
}
