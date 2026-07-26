'use client';

import { useEffect, useState } from 'react';
import dynamic from 'next/dynamic';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useAuth, useClerk, useUser } from '@clerk/nextjs';
import type { SavedRouteDetail } from '@/types/route';
import ConfirmModal from '@/components/ConfirmModal';

const RouteMap = dynamic(() => import('@/components/RouteMap'), { ssr: false });

function formatTimestamp(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}T${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

export default function RouteDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { isLoaded, isSignedIn } = useUser();
  const { getToken } = useAuth();
  const { openSignIn } = useClerk();
  const router = useRouter();

  const [route, setRoute] = useState<SavedRouteDetail | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [shareToken, setShareToken] = useState<string | null>(null);
  const [isSharing, setIsSharing] = useState(false);
  const [shareError, setShareError] = useState<string | null>(null);
  const [isCopied, setIsCopied] = useState(false);

  useEffect(() => {
    if (!isLoaded) return;
    if (!isSignedIn) {
      router.replace('/');
      openSignIn();
      return;
    }

    (async () => {
      setError(null);
      setNotFound(false);
      try {
        const token = await getToken();
        const res = await fetch(`/api/routes/${id}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (res.status === 404) {
          setNotFound(true);
          return;
        }
        if (!res.ok) throw new Error(`Failed to load route: ${res.status}`);
        const data = await res.json() as SavedRouteDetail;
        setRoute(data);
        setShareToken(data.shareToken);
      } catch {
        setError('Could not load this route. Please try again.');
      }
    })();
  }, [isLoaded, isSignedIn, id, getToken, openSignIn, router]);

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

  async function handleConfirmDelete() {
    setIsDeleting(true);
    setDeleteError(null);
    try {
      const token = await getToken();
      const res = await fetch(`/api/routes/${id}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.status === 204 || res.status === 404) {
        router.replace('/my-routes');
        return;
      }
      throw new Error(`Delete failed: ${res.status}`);
    } catch {
      setDeleteError('Could not delete this route. Please try again.');
      setShowDeleteConfirm(false);
    } finally {
      setIsDeleting(false);
    }
  }

  async function handleShare() {
    setIsSharing(true);
    setShareError(null);
    try {
      const token = await getToken();
      const res = await fetch(`/api/routes/${id}/share`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) throw new Error(`Share failed: ${res.status}`);
      const data = await res.json() as { token: string };
      setShareToken(data.token);
    } catch {
      setShareError('Could not share this route. Please try again.');
    } finally {
      setIsSharing(false);
    }
  }

  async function handleStopSharing() {
    setIsSharing(true);
    setShareError(null);
    try {
      const token = await getToken();
      const res = await fetch(`/api/routes/${id}/share`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok && res.status !== 404) throw new Error(`Stop sharing failed: ${res.status}`);
      setShareToken(null);
      setIsCopied(false);
    } catch {
      setShareError('Could not stop sharing this route. Please try again.');
    } finally {
      setIsSharing(false);
    }
  }

  async function handleCopy() {
    if (!shareToken) return;
    try {
      await navigator.clipboard.writeText(`${window.location.origin}/r/${shareToken}`);
      setIsCopied(true);
      setTimeout(() => setIsCopied(false), 2000);
    } catch {
      setShareError('Could not copy the link. Please copy it manually.');
    }
  }

  if (!isLoaded || !isSignedIn) return null;

  if (notFound) {
    return (
      <div className="mx-auto max-w-2xl p-6">
        <p className="text-sm text-zinc-500">Route not found.</p>
        <Link href="/my-routes" className="mt-2 inline-block text-sm font-medium text-blue-600 hover:underline">
          Back to My Routes
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
        <Link href="/my-routes" className="text-sm font-medium text-blue-600 hover:underline">
          ← Back to My Routes
        </Link>
        <h1 className="mt-2 text-xl font-bold text-zinc-900">{route.name}</h1>
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
        {shareToken ? (
          <div className="mt-3 flex flex-col gap-2">
            <div className="flex gap-2">
              <input
                type="text"
                readOnly
                value={`${typeof window !== 'undefined' ? window.location.origin : ''}/r/${shareToken}`}
                className="w-full rounded-md border border-zinc-300 bg-zinc-50 px-3 py-2 text-sm text-zinc-700"
              />
              <button
                onClick={handleCopy}
                className="shrink-0 rounded-md border border-zinc-300 bg-white px-3 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50"
              >
                {isCopied ? 'Copied!' : 'Copy'}
              </button>
            </div>
            <button
              onClick={handleStopSharing}
              disabled={isSharing}
              className="self-start rounded-md px-2 py-1 text-sm font-medium text-zinc-600 hover:bg-zinc-100 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {isSharing ? 'Stopping…' : 'Stop sharing'}
            </button>
          </div>
        ) : (
          <button
            onClick={handleShare}
            disabled={isSharing}
            className="mt-3 w-full rounded-md border border-zinc-300 bg-white px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isSharing ? 'Sharing…' : 'Share'}
          </button>
        )}
        {shareError && (
          <p className="mt-2 text-sm text-red-600">{shareError}</p>
        )}
        <button
          onClick={() => setShowDeleteConfirm(true)}
          className="mt-2 self-start rounded-md px-2 py-1 text-sm font-medium text-red-600 hover:bg-red-50"
        >
          Delete route
        </button>
        {deleteError && (
          <p className="mt-2 text-sm text-red-600">{deleteError}</p>
        )}
      </div>

      <div className="h-[60vh] md:h-full md:flex-1">
        <RouteMap startPoint={null} routeCoordinates={route.geometry.coordinates} />
      </div>

      {showDeleteConfirm && (
        <ConfirmModal
          title="Delete route?"
          message={`Delete "${route.name}"? This cannot be undone.`}
          confirmLabel="Delete"
          isConfirming={isDeleting}
          onConfirm={handleConfirmDelete}
          onCancel={() => setShowDeleteConfirm(false)}
        />
      )}
    </div>
  );
}
