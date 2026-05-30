import type { RouteResult } from '@/types/route';

export async function fetchRoutePreview(): Promise<RouteResult> {
  const url = `${process.env.VELO_API_URL}/routes/preview`;
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) {
    throw new Error(`Backend returned ${res.status} for ${url}`);
  }
  return res.json() as Promise<RouteResult>;
}
