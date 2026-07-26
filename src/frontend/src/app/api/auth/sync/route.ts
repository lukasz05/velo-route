import { requireAuthHeader, proxyFetch } from '@/lib/apiProxy';

export async function POST(request: Request) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  const res = await proxyFetch('/auth/sync', {
    method: 'POST',
    headers: { Authorization: authHeader },
  });
  if (!res.ok) return res;

  return new Response(null, { status: res.status });
}
