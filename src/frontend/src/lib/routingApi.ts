import type { RouteResult, LoopRouteRequest } from '@/types/route';
import { RouteGenerationError } from '@/types/route';

export { RouteGenerationError };

export async function fetchRoutePreview(): Promise<RouteResult> {
  if (!process.env.VELO_API_URL) throw new Error('VELO_API_URL is not set');
  const url = `${process.env.VELO_API_URL}/routes/preview`;
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) {
    throw new Error(`Backend returned ${res.status} for ${url}`);
  }
  return res.json() as Promise<RouteResult>;
}

export async function generateLoopRoute(params: LoopRouteRequest): Promise<RouteResult> {
  const apiUrl = process.env.VELO_API_URL ?? 'http://localhost:5098';
  const url = `${apiUrl}/routes/loop`;
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      startLon: params.startLon,
      startLat: params.startLat,
      minKm: params.minKm,
      maxKm: params.maxKm,
      seed: params.seed ?? null,
    }),
    cache: 'no-store',
  });

  if (!res.ok) {
    let code = 'PROVIDER_ERROR';
    let message = `Backend returned ${res.status}`;
    try {
      const body = await res.json() as { error?: string; code?: string };
      if (body.code) code = body.code;
      if (body.error) message = body.error;
    } catch { /* ignore parse errors */ }
    throw new RouteGenerationError(code, message);
  }

  return res.json() as Promise<RouteResult>;
}

