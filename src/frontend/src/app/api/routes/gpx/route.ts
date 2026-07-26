import { proxyFetch } from '@/lib/apiProxy';

export async function POST(request: Request) {
  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return Response.json({ error: 'Invalid request body', code: 'INVALID_REQUEST' }, { status: 400 });
  }

  const res = await proxyFetch('/routes/gpx', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) return res;

  const gpxText = await res.text();
  return new Response(gpxText, {
    headers: { 'Content-Type': 'application/gpx+xml' },
  });
}
