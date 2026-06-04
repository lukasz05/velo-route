'use client';

import { useState } from 'react';
import type { RouteResult } from '@/types/route';

function formatTimestamp(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}T${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

export default function RouteInfoPanel({ route }: { route: RouteResult }) {
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const km = (route.distanceMeters / 1000).toFixed(1);

  async function handleDownload() {
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

  return (
    <div className="mt-4 rounded-lg border border-zinc-200 bg-zinc-50 p-4">
      <p className="text-sm text-zinc-500">Total distance</p>
      <p className="text-2xl font-semibold text-zinc-900">{km} km</p>
      <button
        onClick={handleDownload}
        disabled={isDownloading}
        className="mt-3 w-full rounded-md border border-zinc-300 bg-white px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {isDownloading ? 'Downloading\u2026' : 'Download GPX'}
      </button>
      {downloadError && (
        <p className="mt-2 text-sm text-red-600">{downloadError}</p>
      )}
    </div>
  );
}

