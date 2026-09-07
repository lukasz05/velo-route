'use client';

import { useEffect, useState } from 'react';
import { useAuth, useUser } from '@clerk/nextjs';
import type { RouteResult } from '@/types/route';

function formatTimestamp(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}T${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

function formatDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function defaultName(route: RouteResult): string {
  return `${formatDate(new Date())} • ${Math.round(route.distanceMeters / 1000)} km`;
}

export default function RouteInfoPanel({ route }: { route: RouteResult }) {
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [name, setName] = useState(() => defaultName(route));
  const [tags, setTags] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [isSaved, setIsSaved] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const { isSignedIn } = useUser();
  const { getToken } = useAuth();
  const km = (route.distanceMeters / 1000).toFixed(1);

  useEffect(() => {
    setName(defaultName(route));
    setTags('');
    setIsSaved(false);
    setSaveError(null);
  }, [route]);

  async function handleSave() {
    setIsSaving(true);
    setSaveError(null);
    try {
      const token = await getToken();
      const res = await fetch('/api/routes', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          name,
          tags: tags.split(',').map((t) => t.trim()).filter(Boolean) || undefined,
          distanceKm: route.distanceMeters / 1000,
          coordinates: route.geometry.coordinates,
        }),
      });
      if (!res.ok) throw new Error(`Save failed: ${res.status}`);
      setIsSaved(true);
    } catch {
      setSaveError('Save failed. Please try again.');
    } finally {
      setIsSaving(false);
    }
  }

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
      <p className="mt-2 text-sm text-zinc-500">Surface quality</p>
      <p className="text-sm font-medium text-zinc-900">
        {route.segments.length === 0 ? 'Unknown' : `${Math.round(route.pavedRatio * 100)}% paved`}
      </p>
      {route.qualityWarning && (
        <p className="mt-2 text-sm text-amber-600" role="status">
          This route has more overlap/backtracking than usual for the area — the road network
          left few better options.
        </p>
      )}
      {isSignedIn && (
        <>
          <label className="mt-3 block text-sm text-zinc-500" htmlFor="route-name">
            Name
          </label>
          <input
            id="route-name"
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={isSaved}
            className="mt-1 w-full rounded-md border border-zinc-300 px-3 py-2 text-sm text-zinc-900 disabled:cursor-not-allowed disabled:opacity-50"
          />
          <label className="mt-2 block text-sm text-zinc-500" htmlFor="route-tags">
            Tags
          </label>
          <input
            id="route-tags"
            type="text"
            placeholder="scenic, hilly"
            value={tags}
            onChange={(e) => setTags(e.target.value)}
            disabled={isSaved}
            className="mt-1 w-full rounded-md border border-zinc-300 px-3 py-2 text-sm text-zinc-900 disabled:cursor-not-allowed disabled:opacity-50"
          />
          <button
            onClick={handleSave}
            disabled={isSaving || isSaved}
            className="mt-3 w-full rounded-md border border-zinc-300 bg-white px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isSaved ? 'Saved \u2713' : isSaving ? 'Saving\u2026' : 'Save'}
          </button>
          {saveError && (
            <p className="mt-2 text-sm text-red-600">{saveError}</p>
          )}
        </>
      )}
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

