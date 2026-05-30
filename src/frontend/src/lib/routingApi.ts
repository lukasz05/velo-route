import type { RouteResult } from '@/types/route';

export async function fetchRoutePreview(): Promise<RouteResult> {
  if (!process.env.VELO_API_URL) throw new Error('VELO_API_URL is not set');
  const url = `${process.env.VELO_API_URL}/routes/preview`;
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) {
    throw new Error(`Backend returned ${res.status} for ${url}`);
  }
  return res.json() as Promise<RouteResult>;
}
