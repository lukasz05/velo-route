import { requireAuthHeader, proxyFetch } from '@/lib/apiProxy';

export async function GET(request: Request) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  const res = await proxyFetch('/routes', {
    headers: { Authorization: authHeader },
  });
  if (!res.ok) return res;

  const resBody = await res.json();
  return Response.json(resBody, { status: res.status });
}

export async function POST(request: Request) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return Response.json({ error: 'Invalid request body', code: 'INVALID_REQUEST' }, { status: 400 });
  }

  const res = await proxyFetch('/routes', {
    method: 'POST',
    headers: { Authorization: authHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) return res;

  const resBody = await res.json();
  return Response.json(resBody, { status: res.status });
}
